import { pathToFileURL } from "node:url";

function send(message) {
  return new Promise((resolve, reject) => {
    if (process.send === undefined) {
      reject(new Error("Preview source runner requires an IPC channel."));
      return;
    }
    process.send(message, (error) => (error ? reject(error) : resolve()));
  });
}

function serializedError(error) {
  if (error instanceof Error) {
    return {
      type: "error",
      message: error.message,
      ...(error.stack === undefined ? {} : { stack: error.stack })
    };
  }
  return {
    type: "error",
    message: String(error)
  };
}

try {
  const sourceFile = process.argv[2];
  if (sourceFile === undefined) {
    throw new Error("A preview source module path is required.");
  }
  const sourceModule = await import(pathToFileURL(sourceFile).href);
  const exported = sourceModule.default ?? sourceModule.document;
  if (exported === undefined) {
    throw new Error(
      "Preview source must default-export a UDOM document (a named `document` export is also accepted)."
    );
  }
  const value = typeof exported === "function" ? await exported() : await exported;
  const document = JSON.parse(JSON.stringify(value));
  await send({ type: "result", document });
} catch (error) {
  await send(serializedError(error));
}

process.disconnect?.();
