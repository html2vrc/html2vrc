import { watch, type FSWatcher } from "node:fs";
import { readFile, realpath, stat } from "node:fs/promises";
import { createServer, type ServerResponse } from "node:http";
import { dirname, extname, isAbsolute, relative, resolve, sep } from "node:path";
import type { AddressInfo } from "node:net";
import { renderPreviewToHTML } from "./preview.js";
import type { UdomDocument } from "./types.js";

const EVENTS_PATH = "/__html2vrc/events";
const LOCAL_ASSET_ORIGIN = "http://html2vrc.local";

export interface PreviewServerOptions {
  file: string;
  host?: string;
  port?: number;
  title?: string;
}

export interface PreviewServer {
  readonly host: string;
  readonly port: number;
  readonly url: string;
  close(): Promise<void>;
}

const MIME_TYPES: Readonly<Record<string, string>> = {
  ".avif": "image/avif",
  ".gif": "image/gif",
  ".jpeg": "image/jpeg",
  ".jpg": "image/jpeg",
  ".otf": "font/otf",
  ".png": "image/png",
  ".svg": "image/svg+xml",
  ".ttf": "font/ttf",
  ".webp": "image/webp",
  ".woff": "font/woff",
  ".woff2": "font/woff2"
};

function escapeHtml(value: string): string {
  return value
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

function send(
  response: ServerResponse,
  status: number,
  contentType: string,
  body: string | Buffer,
  headOnly = false
): void {
  response.writeHead(status, {
    "cache-control": "no-store",
    "content-type": contentType,
    "x-content-type-options": "nosniff"
  });
  response.end(headOnly ? undefined : body);
}

function errorDocument(error: unknown): string {
  const message = error instanceof Error ? error.message : String(error);
  const diagnostics =
    error instanceof Error && "diagnostics" in error
      ? JSON.stringify(error.diagnostics, null, 2)
      : "";
  return `<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>HTML2VRC Preview Error</title><style>body{margin:0;padding:32px;background:#18181b;color:#fafafa;font:15px/1.55 ui-monospace,SFMono-Regular,Consolas,monospace}main{max-width:960px;margin:auto}h1{font:600 24px/1.2 system-ui,sans-serif;color:#fca5a5}pre{padding:16px;overflow:auto;border:1px solid #3f3f46;border-radius:8px;background:#09090b;white-space:pre-wrap}</style></head><body><main><h1>Preview could not be rendered</h1><pre>${escapeHtml(message)}${diagnostics === "" ? "" : `\n\n${escapeHtml(diagnostics)}`}</pre><p>Fix and save the UDOM file. This page will reload automatically.</p></main><script>new EventSource(${JSON.stringify(EVENTS_PATH)}).addEventListener("reload",()=>location.reload())</script></body></html>`;
}

function localPath(sourceDirectory: string, pathname: string): string | undefined {
  let decoded: string;
  try {
    decoded = decodeURIComponent(pathname);
  } catch {
    return undefined;
  }
  if (decoded.includes("\0")) {
    return undefined;
  }
  const segments = decoded.split("/").filter((segment) => segment.length > 0);
  const candidate = resolve(sourceDirectory, segments.join(sep));
  const pathFromSource = relative(sourceDirectory, candidate);
  if (pathFromSource === "" || pathFromSource.startsWith(`..${sep}`) || pathFromSource === ".." || isAbsolute(pathFromSource)) {
    return undefined;
  }
  return candidate;
}

function isWithin(directory: string, candidate: string): boolean {
  const pathFromDirectory = relative(directory, candidate);
  return pathFromDirectory !== ".." && !pathFromDirectory.startsWith(`..${sep}`) && !isAbsolute(pathFromDirectory);
}

function isDeclaredResource(document: UdomDocument, pathname: string): boolean {
  return (document.resources ?? []).some((resource) => {
    try {
      const resourceUrl = new URL(resource.uri, `${LOCAL_ASSET_ORIGIN}/`);
      return resourceUrl.origin === LOCAL_ASSET_ORIGIN && resourceUrl.pathname === pathname;
    } catch {
      return false;
    }
  });
}

function watchSource(directory: string, file: string, reload: () => void): FSWatcher {
  try {
    return watch(directory, { recursive: true }, reload);
  } catch {
    return watch(file, reload);
  }
}

/** Start a local UDOM preview server with asset serving and live reload. */
export async function startPreviewServer(
  options: PreviewServerOptions
): Promise<PreviewServer> {
  const sourceFile = await realpath(resolve(options.file));
  const sourceStats = await stat(sourceFile);
  if (!sourceStats.isFile()) {
    throw new Error(`UDOM preview source is not a file: ${sourceFile}`);
  }

  const sourceDirectory = dirname(sourceFile);
  const host = options.host ?? "127.0.0.1";
  const requestedPort = options.port ?? 4173;
  if (!Number.isInteger(requestedPort) || requestedPort < 0 || requestedPort > 65_535) {
    throw new Error(`Invalid preview server port: ${requestedPort}`);
  }
  const clients = new Set<ServerResponse>();
  let watcher: FSWatcher | undefined;
  let reloadTimer: NodeJS.Timeout | undefined;
  let closed = false;

  const notifyReload = (): void => {
    if (reloadTimer !== undefined) {
      clearTimeout(reloadTimer);
    }
    reloadTimer = setTimeout(() => {
      reloadTimer = undefined;
      for (const client of clients) {
        client.write("event: reload\ndata: {}\n\n");
      }
    }, 35);
  };

  const server = createServer(async (request, response) => {
    const method = request.method ?? "GET";
    const headOnly = method === "HEAD";
    if (method !== "GET" && !headOnly) {
      send(response, 405, "text/plain; charset=utf-8", "Method not allowed\n");
      return;
    }

    const requestUrl = new URL(request.url ?? "/", "http://localhost");
    if (requestUrl.pathname === EVENTS_PATH) {
      if (headOnly) {
        send(response, 405, "text/plain; charset=utf-8", "Method not allowed\n", true);
        return;
      }
      response.writeHead(200, {
        "cache-control": "no-cache, no-transform",
        connection: "keep-alive",
        "content-type": "text/event-stream; charset=utf-8",
        "x-accel-buffering": "no"
      });
      response.write(": connected\n\n");
      clients.add(response);
      request.on("close", () => clients.delete(response));
      return;
    }

    if (requestUrl.pathname === "/" || requestUrl.pathname === "/index.html") {
      try {
        const source = await readFile(sourceFile, "utf8");
        const document = JSON.parse(source) as UdomDocument;
        const html = renderPreviewToHTML(document, {
          liveReloadPath: EVENTS_PATH,
          ...(options.title === undefined ? {} : { title: options.title })
        });
        send(response, 200, "text/html; charset=utf-8", html, headOnly);
      } catch (error) {
        send(response, 422, "text/html; charset=utf-8", errorDocument(error), headOnly);
      }
      return;
    }

    let document: UdomDocument;
    try {
      document = JSON.parse(await readFile(sourceFile, "utf8")) as UdomDocument;
    } catch {
      send(response, 403, "text/plain; charset=utf-8", "Forbidden\n", headOnly);
      return;
    }
    if (!isDeclaredResource(document, requestUrl.pathname)) {
      send(response, 403, "text/plain; charset=utf-8", "Forbidden\n", headOnly);
      return;
    }

    const candidatePath = localPath(sourceDirectory, requestUrl.pathname);
    if (candidatePath === undefined) {
      send(response, 403, "text/plain; charset=utf-8", "Forbidden\n", headOnly);
      return;
    }
    try {
      const assetPath = await realpath(candidatePath);
      if (!isWithin(sourceDirectory, assetPath) || assetPath === sourceFile) {
        send(response, 403, "text/plain; charset=utf-8", "Forbidden\n", headOnly);
        return;
      }
      const assetStats = await stat(assetPath);
      if (!assetStats.isFile()) {
        send(response, 404, "text/plain; charset=utf-8", "Not found\n", headOnly);
        return;
      }
      const body = await readFile(assetPath);
      const contentType = MIME_TYPES[extname(assetPath).toLowerCase()] ?? "application/octet-stream";
      send(response, 200, contentType, body, headOnly);
    } catch (error) {
      const code = error instanceof Error && "code" in error ? error.code : undefined;
      send(
        response,
        code === "ENOENT" ? 404 : 500,
        "text/plain; charset=utf-8",
        code === "ENOENT" ? "Not found\n" : "Could not read asset\n",
        headOnly
      );
    }
  });

  await new Promise<void>((resolveListening, reject) => {
    const onError = (error: Error): void => {
      server.off("listening", onListening);
      reject(error);
    };
    const onListening = (): void => {
      server.off("error", onError);
      resolveListening();
    };
    server.once("error", onError);
    server.once("listening", onListening);
    server.listen(requestedPort, host);
  });

  try {
    watcher = watchSource(sourceDirectory, sourceFile, notifyReload);
  } catch (error) {
    await new Promise<void>((resolveClose) => server.close(() => resolveClose()));
    throw error;
  }
  watcher.on("error", () => watcher?.close());

  const address = server.address() as AddressInfo | null;
  if (address === null) {
    await new Promise<void>((resolveClose) => server.close(() => resolveClose()));
    throw new Error("Preview server did not expose a listening address.");
  }
  const urlHost = host.includes(":") ? `[${host}]` : host;
  const url = `http://${urlHost}:${address.port}/`;

  return {
    host,
    port: address.port,
    url,
    async close(): Promise<void> {
      if (closed) {
        return;
      }
      closed = true;
      watcher?.close();
      if (reloadTimer !== undefined) {
        clearTimeout(reloadTimer);
      }
      for (const client of clients) {
        client.end();
      }
      clients.clear();
      await new Promise<void>((resolveClose, reject) => {
        server.close((error) => (error ? reject(error) : resolveClose()));
      });
    }
  };
}

export const previewEventsPath = EVENTS_PATH;
