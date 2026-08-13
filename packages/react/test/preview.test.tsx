import assert from "node:assert/strict";
import { mkdir, mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { get, type ClientRequest } from "node:http";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import {
  UdomPreviewError,
  renderPreviewToHTML
} from "../src/index.js";
import {
  previewEventsPath,
  startPreviewServer
} from "../src/preview-server.js";
import type { UdomDocument } from "../src/types.js";

const basic = JSON.parse(
  await readFile(new URL("./fixtures/basic.udom.json", import.meta.url), "utf8")
) as UdomDocument;
const controls = JSON.parse(
  await readFile(new URL("./fixtures/controls.udom.json", import.meta.url), "utf8")
) as UdomDocument;

test("renders canonical UDOM as deterministic standalone HTML", () => {
  const first = renderPreviewToHTML(basic, { title: "Preview <one>" });
  const second = renderPreviewToHTML(basic, { title: "Preview <one>" });

  assert.equal(first, second);
  assert.match(first, /^<!doctype html>/u);
  assert.match(first, /<title>Preview &lt;one&gt;<\/title>/u);
  assert.match(first, /data-udom-width="1200"/u);
  assert.match(first, /data-udom-height="720"/u);
  assert.match(first, /data-udom-fit="contain"/u);
  assert.match(first, /id="screen"[^>]+display:flex/u);
  assert.match(first, /\.h2vrc-text\{overflow:hidden;color:#000000FF;font-size:16px/u);
  assert.match(first, /<img[^>]+id="logo"[^>]+src="assets\/logo\.png"/u);
  assert.match(first, /<button type="button"[^>]+id="continue"/u);
  assert.match(first, /id="title"[^>]*><span class="h2vrc-text-content">Hello VRChat<\/span>/u);
  assert.match(first, /data-udom-on="\{&quot;activate&quot;:&quot;menu\.continue&quot;/u);
  assert.doesNotMatch(first, /new Function|eval\(/u);
});

test("previews every control with explicit falsy values and metadata", () => {
  const html = renderPreviewToHTML(controls, {
    liveReloadPath: previewEventsPath
  });

  assert.match(html, /<input type="checkbox"(?![^>]* checked)[^>]*>/u);
  assert.match(html, /<input type="range" min="0" max="100" value="0" step="any"/u);
  assert.match(html, /<label[^>]+id="profile-name"[^>]*><input type="text"[^>]+placeholder="Name"[^>]+value=""/u);
  assert.match(html, /data-scroll-axis="both" data-scroll-x="12" data-scroll-y="24"/u);
  assert.match(html, /data-udom-object="world\.avatarPreview"/u);
  assert.match(html, />Avatar unavailable<\/span>/u);
  assert.match(html, /const liveReloadPath="\/__html2vrc\/events"/u);
  assert.match(html, /data-udom-bind="\{&quot;checked&quot;:&quot;settings\.music&quot;\}"/u);
});

test("merges style refs deeply and preserves UDOM paint and transform order", () => {
  const document: UdomDocument = {
    asset: { version: "0.1" },
    viewport: { width: 320, height: 180, fit: "stretch" },
    styles: [
      {
        id: "base",
        style: {
          layout: {
            mode: "flex",
            padding: { top: 1, right: 2, bottom: 3, left: 4 }
          },
          text: { font: "font.preview", fontSize: 18, color: "#FFFFFFFF" }
        }
      },
      {
        id: "override",
        style: {
          layout: { padding: { left: 8 } },
          text: { fontSize: 20 }
        }
      }
    ],
    resources: [
      {
        id: "font.preview",
        type: "font",
        uri: "assets/preview.woff2",
        mimeType: "font/woff2"
      }
    ],
    root: {
      type: "text",
      id: "styled",
      value: "Styled",
      styleRefs: ["base", "override"],
      style: {
        layout: { padding: { top: 9 } },
        paint: {
          backgrounds: [
            { type: "color", color: "#112233FF" },
            {
              type: "linear-gradient",
              angle: 90,
              stops: [
                { position: 0, color: "#000000FF" },
                { position: 1, color: "#FFFFFFFF" }
              ]
            }
          ],
          shadows: [
            { offsetX: 1, color: "#111111FF" },
            { offsetX: 2, color: "#222222FF" }
          ]
        },
        text: { fontSize: 22 },
        transform: {
          operations: [
            { type: "translate", x: 5, y: "10%" },
            { type: "rotate", degrees: 15 },
            { type: "scale", x: 2, y: 3 }
          ]
        }
      }
    }
  };

  const html = renderPreviewToHTML(document);
  assert.match(html, /padding:9px 2px 3px 8px/u);
  assert.match(html, /font-size:22px/u);
  assert.match(html, /font-family:&quot;udom-font\.preview&quot;/u);
  assert.match(html, /background:linear-gradient\(90deg,#000000FF 0%,#FFFFFFFF 100%\),linear-gradient\(#112233FF,#112233FF\)/u);
  assert.match(html, /box-shadow:2px 0px 0px 0px #222222FF,1px 0px 0px 0px #111111FF/u);
  assert.match(html, /transform:scale\(2,3\) rotate\(15deg\) translate\(5px,10%\)/u);
  assert.match(html, /@font-face\{font-family:"udom-font\.preview";src:url\("assets\/preview\.woff2"\)/u);
});

test("escapes document content and refuses active resource schemes", () => {
  const document: UdomDocument = {
    asset: { version: "0.1" },
    viewport: { width: 100, height: 100 },
    resources: [
      { id: "unsafe", type: "image", uri: "javascript:alert(1)" },
      {
        id: "font.escape",
        type: "font",
        uri: "assets/</style><script>fontPwned()</script>.woff2"
      }
    ],
    root: {
      type: "element",
      id: "root",
      name: "view",
      children: [
        {
          type: "text",
          id: "message",
          value: "</div><script>globalThis.pwned = true</script>",
          style: { text: { font: "font.escape" } }
        },
        {
          type: "element",
          id: "unsafe-image",
          name: "image",
          properties: { resource: "unsafe" }
        }
      ]
    }
  };

  const html = renderPreviewToHTML(document, { title: "</title><script>bad</script>" });
  assert.match(html, /&lt;\/div&gt;&lt;script&gt;globalThis\.pwned = true&lt;\/script&gt;/u);
  assert.match(html, /<title>&lt;\/title&gt;&lt;script&gt;bad&lt;\/script&gt;<\/title>/u);
  assert.match(html, /id="unsafe-image"[^>]+src=""[^>]+data-preview-missing-resource/u);
  assert.doesNotMatch(html, /javascript:alert/u);
  assert.doesNotMatch(html, /<\/style><script>fontPwned/u);
  assert.match(html, /assets\/\\3C \/style\\3E \\3C script\\3E fontPwned/u);
});

test("rejects invalid UDOM before rendering", () => {
  const invalid = structuredClone(basic);
  const image = invalid.root.type === "element" ? invalid.root.children?.[1] : undefined;
  if (image?.type === "element") {
    image.properties = { resource: "missing" };
  }

  assert.throws(
    () => renderPreviewToHTML(invalid),
    (error: unknown) => {
      assert.ok(error instanceof UdomPreviewError);
      assert.equal(error.diagnostics[0]?.code, "H2VRC-RESOURCE-002");
      return true;
    }
  );
});

test("keeps flow offsets inert, absolute offsets explicit, and text-input children", () => {
  const document: UdomDocument = {
    asset: { version: "0.1" },
    viewport: { width: 400, height: 200 },
    root: {
      type: "element",
      id: "root",
      name: "view",
      style: { layout: { mode: "flex" } },
      children: [
        {
          type: "element",
          id: "flow",
          name: "view",
          style: { layout: { x: 30, y: 40 } }
        },
        {
          type: "element",
          id: "absolute",
          name: "view",
          style: { layout: { position: "absolute", x: 50, y: 60 } }
        },
        {
          type: "element",
          id: "custom-input",
          name: "text-input",
          properties: { value: "Player" },
          children: [
            { type: "text", id: "input-decoration", value: "Edit" }
          ]
        },
        {
          type: "text",
          id: "hidden",
          value: "Hidden",
          style: {
            layout: { mode: "none" },
            text: { verticalAlign: "middle" }
          }
        }
      ]
    }
  };

  const html = renderPreviewToHTML(document);
  assert.match(html, /class="[^"]*h2vrc-position-flow" id="flow"[^>]+--h2vrc-x:30px;--h2vrc-y:40px/u);
  assert.match(html, /class="[^"]*h2vrc-position-absolute" id="absolute"[^>]+--h2vrc-x:50px;--h2vrc-y:60px/u);
  assert.match(html, /\.h2vrc-mode-absolute>\.h2vrc-node,\.h2vrc-position-absolute\{position:absolute/u);
  assert.match(html, /\.h2vrc-mode-none\{display:none!important\}/u);
  assert.match(html, /id="custom-input"[^>]*>.*id="input-decoration"/u);
});

test("renders ellipsis on a bounded inner text box", () => {
  const document: UdomDocument = {
    asset: { version: "0.1" },
    viewport: { width: 320, height: 180 },
    root: {
      type: "text",
      id: "ellipsis",
      value: "A deliberately long single line",
      style: {
        layout: { width: 120, height: 40 },
        text: {
          align: "end",
          verticalAlign: "bottom",
          wrap: "nowrap",
          overflow: "ellipsis"
        }
      }
    }
  };

  const html = renderPreviewToHTML(document);
  assert.match(html, /class="[^"]*h2vrc-text-vertical h2vrc-text-ellipsis h2vrc-text-wrap-nowrap"/u);
  assert.match(html, /\.h2vrc-text-ellipsis>\.h2vrc-text-content\{overflow:hidden;text-overflow:ellipsis\}/u);
  assert.match(html, /data-preview-overflowing/u);
});

interface ReloadConnection {
  request: ClientRequest;
  ready: Promise<void>;
  reloaded: Promise<void>;
}

function connectToReload(url: string): ReloadConnection {
  let resolveReady: (() => void) | undefined;
  let rejectReady: ((error: Error) => void) | undefined;
  let resolveReloaded: (() => void) | undefined;
  let rejectReloaded: ((error: Error) => void) | undefined;
  const ready = new Promise<void>((resolvePromise, rejectPromise) => {
    resolveReady = resolvePromise;
    rejectReady = rejectPromise;
  });
  const reloaded = new Promise<void>((resolvePromise, rejectPromise) => {
    resolveReloaded = resolvePromise;
    rejectReloaded = rejectPromise;
  });
  const request = get(new URL(previewEventsPath, url), (response) => {
    response.setEncoding("utf8");
    let received = "";
    response.on("data", (chunk: string) => {
      received += chunk;
      if (received.includes(": connected")) {
        resolveReady?.();
      }
      if (received.includes("event: reload")) {
        resolveReloaded?.();
      }
    });
  });
  request.on("error", (error) => {
    rejectReady?.(error);
    rejectReloaded?.(error);
  });
  return { request, ready, reloaded };
}

async function within<T>(promise: Promise<T>, milliseconds = 3_000): Promise<T> {
  let timer: NodeJS.Timeout | undefined;
  try {
    return await Promise.race([
      promise,
      new Promise<never>((_resolve, reject) => {
        timer = setTimeout(() => reject(new Error("Timed out waiting for preview server.")), milliseconds);
      })
    ]);
  } finally {
    if (timer !== undefined) {
      clearTimeout(timer);
    }
  }
}

test("serves previews and relative assets and reloads on file changes", { timeout: 10_000 }, async () => {
  const directory = await mkdtemp(join(tmpdir(), "html2vrc-preview-"));
  const sourceFile = join(directory, "preview.udom.json");
  const assetDirectory = join(directory, "assets");
  const assetFile = join(assetDirectory, "logo.png");
  const undeclaredFile = join(directory, "secret.txt");
  await mkdir(assetDirectory);
  await writeFile(sourceFile, `${JSON.stringify(basic, null, 2)}\n`, "utf8");
  await writeFile(assetFile, Buffer.from([1, 2, 3]));
  await writeFile(undeclaredFile, "not public", "utf8");

  const preview = await startPreviewServer({ file: sourceFile, port: 0 });
  let reload: ReloadConnection | undefined;
  try {
    const page = await fetch(preview.url);
    assert.equal(page.status, 200);
    assert.match(await page.text(), /const liveReloadPath="\/__html2vrc\/events"/u);

    const asset = await fetch(new URL("assets/logo.png", preview.url));
    assert.equal(asset.status, 200);
    assert.equal(asset.headers.get("content-type"), "image/png");
    assert.deepEqual(Buffer.from(await asset.arrayBuffer()), Buffer.from([1, 2, 3]));

    const source = await fetch(new URL("preview.udom.json", preview.url));
    assert.equal(source.status, 403);
    const undeclared = await fetch(new URL("secret.txt", preview.url));
    assert.equal(undeclared.status, 403);
    const traversal = await fetch(new URL("..%2Foutside.txt", preview.url));
    assert.equal(traversal.status, 403);

    reload = connectToReload(preview.url);
    await within(reload.ready);
    await writeFile(assetFile, Buffer.from([4, 5, 6]));
    await within(reload.reloaded);

    await writeFile(sourceFile, "not json", "utf8");
    const invalid = await fetch(preview.url);
    assert.equal(invalid.status, 422);
    assert.match(await invalid.text(), /Preview could not be rendered/u);
  } finally {
    reload?.request.destroy();
    await preview.close();
    await rm(directory, { recursive: true, force: true });
  }
});
