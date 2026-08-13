#!/usr/bin/env node

import { startPreviewServer } from "../dist/preview-server.js";

function usage() {
  return `Usage: udom-preview <document.udom.json|source.tsx> [--host <host>] [--port <port>] [--source-timeout <ms>] [--title <title>]

Serves canonical UDOM JSON or a JS/TS module that exports UDOM, with live reload.
Defaults: --host 127.0.0.1 --port 4173`;
}

function parseArguments(arguments_) {
  if (arguments_.includes("--help") || arguments_.includes("-h")) {
    console.log(usage());
    process.exit(0);
  }

  let file;
  const options = {};
  for (let index = 0; index < arguments_.length; index += 1) {
    const argument = arguments_[index];
    if (!argument.startsWith("-")) {
      if (file !== undefined) {
        throw new Error(`Unexpected argument: ${argument}`);
      }
      file = argument;
      continue;
    }
    if (
      argument !== "--host" &&
      argument !== "--port" &&
      argument !== "--source-timeout" &&
      argument !== "--title"
    ) {
      throw new Error(`Unknown option: ${argument}`);
    }
    const value = arguments_[index + 1];
    if (value === undefined) {
      throw new Error(`Missing value for ${argument}`);
    }
    index += 1;
    if (argument === "--host") {
      options.host = value;
    } else if (argument === "--title") {
      options.title = value;
    } else if (argument === "--port") {
      const port = Number(value);
      if (!Number.isInteger(port) || port < 0 || port > 65535) {
        throw new Error(`Invalid port: ${value}`);
      }
      options.port = port;
    } else {
      const sourceTimeoutMs = Number(value);
      if (!Number.isInteger(sourceTimeoutMs) || sourceTimeoutMs <= 0) {
        throw new Error(`Invalid source timeout: ${value}`);
      }
      options.sourceTimeoutMs = sourceTimeoutMs;
    }
  }
  if (file === undefined) {
    throw new Error("A UDOM JSON or JS/TS source path is required.");
  }
  return { file, ...options };
}

try {
  const preview = await startPreviewServer(parseArguments(process.argv.slice(2)));
  console.log(`HTML2VRC preview: ${preview.url}`);
  console.log("Watching the preview source and relative assets. Press Ctrl+C to stop.");

  const stop = async () => {
    await preview.close();
    process.exit(0);
  };
  process.once("SIGINT", stop);
  process.once("SIGTERM", stop);
} catch (error) {
  console.error(error instanceof Error ? error.message : String(error));
  console.error(usage());
  process.exitCode = 1;
}
