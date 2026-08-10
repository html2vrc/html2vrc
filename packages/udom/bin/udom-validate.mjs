#!/usr/bin/env node

import { readFile } from "node:fs/promises";
import { validateUdom } from "../src/validator.mjs";

const args = process.argv.slice(2);
const jsonOutput = args.includes("--json");
const files = args.filter((arg) => arg !== "--json");

if (files.length === 0) {
  console.error("Usage: udom-validate [--json] <file.udom.json> [...]");
  process.exitCode = 2;
} else {
  let failed = false;
  const results = [];

  for (const file of files) {
    try {
      const document = JSON.parse(await readFile(file, "utf8"));
      const result = validateUdom(document);
      results.push({ file, ...result });
      failed ||= !result.valid;
    } catch (error) {
      failed = true;
      results.push({
        file,
        valid: false,
        diagnostics: [
          {
            code: "H2VRC-JSON-001",
            severity: "error",
            pointer: "",
            message: error.message
          }
        ]
      });
    }
  }

  if (jsonOutput) {
    console.log(JSON.stringify(results, null, 2));
  } else {
    for (const result of results) {
      if (result.valid) {
        console.log(`✓ ${result.file}`);
        continue;
      }
      console.error(`✗ ${result.file}`);
      for (const item of result.diagnostics) {
        const location = item.pointer || "/";
        console.error(`  ${item.code} ${location}: ${item.message}`);
      }
    }
  }

  if (failed) {
    process.exitCode = 1;
  }
}
