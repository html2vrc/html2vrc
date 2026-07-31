import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import { validateUdom } from "../src/validator.mjs";

const fixtureRoot = new URL("../fixtures/", import.meta.url);
const manifest = JSON.parse(
  await readFile(new URL("manifest.json", fixtureRoot), "utf8")
);

for (const fixture of manifest) {
  test(`conformance fixture: ${fixture.file}`, async () => {
    const document = JSON.parse(
      await readFile(new URL(fixture.file, fixtureRoot), "utf8")
    );
    const result = validateUdom(document);

    assert.equal(
      result.valid,
      fixture.valid,
      JSON.stringify(result.diagnostics, null, 2)
    );

    if (fixture.code) {
      assert.ok(
        result.diagnostics.some(({ code }) => code === fixture.code),
        `Expected ${fixture.code}, received ${result.diagnostics
          .map(({ code }) => code)
          .join(", ")}`
      );
    }
  });
}

test("a supported required extension is accepted", () => {
  const document = {
    asset: { version: "0.1" },
    viewport: { width: 1280, height: 720 },
    extensionsUsed: ["H2VRC_example"],
    extensionsRequired: ["H2VRC_example"],
    root: {
      type: "element",
      id: "root",
      name: "view",
      extensions: {
        H2VRC_example: {
          enabled: true
        }
      }
    }
  };

  assert.deepEqual(
    validateUdom(document, {
      supportedExtensions: ["H2VRC_example"]
    }),
    {
      valid: true,
      diagnostics: []
    }
  );
});

test("minVersion cannot be greater than version", () => {
  const document = {
    asset: {
      version: "0.1",
      minVersion: "0.2"
    },
    viewport: { width: 1280, height: 720 },
    root: {
      type: "element",
      id: "root",
      name: "view"
    }
  };

  const result = validateUdom(document);
  assert.equal(result.valid, false);
  assert.ok(
    result.diagnostics.some(({ code }) => code === "H2VRC-VERSION-001")
  );
});

test("the 0.1 validator rejects another draft version", () => {
  const document = {
    asset: { version: "0.2" },
    viewport: { width: 1280, height: 720 },
    root: {
      type: "element",
      id: "root",
      name: "view"
    }
  };

  const result = validateUdom(document);
  assert.equal(result.valid, false);
  assert.ok(
    result.diagnostics.some(({ code }) => code === "H2VRC-VERSION-002")
  );
});

test("extras never declare rendering extensions", () => {
  const document = {
    asset: { version: "0.1" },
    viewport: { width: 1280, height: 720 },
    root: {
      type: "element",
      id: "root",
      name: "view",
      extras: {
        extensions: {
          EDITOR_private_state: {
            selected: true
          }
        }
      }
    }
  };

  assert.deepEqual(validateUdom(document), {
    valid: true,
    diagnostics: []
  });
});
