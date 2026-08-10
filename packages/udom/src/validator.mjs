import { readFile } from "node:fs/promises";
import Ajv2020 from "ajv/dist/2020.js";

const schemaUrl = new URL("../schemas/udom-0.1.schema.json", import.meta.url);
const schema = JSON.parse(await readFile(schemaUrl, "utf8"));

const ajv = new Ajv2020({
  allErrors: true,
  strict: true,
  verbose: true
});
const validateSchema = ajv.compile(schema);
const DEFAULT_SUPPORTED_VERSION = "0.1";

const BINDING_SLOTS = {
  text: new Set(["visible", "enabled", "value"]),
  view: new Set(["visible", "enabled"]),
  image: new Set(["visible", "enabled"]),
  button: new Set(["visible", "enabled"]),
  toggle: new Set(["visible", "enabled", "checked"]),
  slider: new Set(["visible", "enabled", "value"]),
  "text-input": new Set(["visible", "enabled", "value"]),
  scroll: new Set(["visible", "enabled", "offset"]),
  embed: new Set(["visible", "enabled"])
};

const EVENTS = {
  view: new Set(),
  image: new Set(),
  button: new Set(["activate", "focus", "blur"]),
  toggle: new Set(["change", "focus", "blur"]),
  slider: new Set(["change", "focus", "blur"]),
  "text-input": new Set(["change", "submit", "focus", "blur"]),
  scroll: new Set(["scroll", "focus", "blur"]),
  embed: new Set()
};

function escapePointer(value) {
  return String(value).replaceAll("~", "~0").replaceAll("/", "~1");
}

function childPointer(pointer, key) {
  return `${pointer}/${escapePointer(key)}`;
}

function compareVersions(left, right) {
  const [leftMajor, leftMinor] = left.split(".").map(Number);
  const [rightMajor, rightMinor] = right.split(".").map(Number);
  return leftMajor - rightMajor || leftMinor - rightMinor;
}

function diagnostic(code, pointer, message, nodeId) {
  return {
    code,
    severity: "error",
    pointer: pointer || "",
    ...(nodeId ? { nodeId } : {}),
    message
  };
}

function walkObject(value, pointer, visitor) {
  if (Array.isArray(value)) {
    value.forEach((item, index) => {
      walkObject(item, childPointer(pointer, index), visitor);
    });
    return;
  }

  if (!value || typeof value !== "object") {
    return;
  }

  visitor(value, pointer);
  for (const [key, item] of Object.entries(value)) {
    if (key === "extras" || key === "extensions") {
      continue;
    }
    walkObject(item, childPointer(pointer, key), visitor);
  }
}

function collectNodes(root) {
  const nodes = [];

  function visit(node, pointer) {
    nodes.push({ node, pointer });
    if (node?.type === "element" && Array.isArray(node.children)) {
      node.children.forEach((child, index) => {
        visit(child, `${pointer}/children/${index}`);
      });
    }
  }

  visit(root, "/root");
  return nodes;
}

function validateUniqueIds(items, kind, code, diagnostics) {
  const seen = new Map();
  for (const { id, pointer } of items) {
    if (!id) {
      continue;
    }
    if (seen.has(id)) {
      diagnostics.push(
        diagnostic(
          code,
          childPointer(pointer, "id"),
          `Duplicate ${kind} ID "${id}"; first declared at ${seen.get(id)}.`
        )
      );
    } else {
      seen.set(id, childPointer(pointer, "id"));
    }
  }
  return seen;
}

function validateResourceReference(
  resourceId,
  pointer,
  allowedTypes,
  resources,
  diagnostics,
  nodeId
) {
  const resource = resources.get(resourceId);
  if (!resource) {
    diagnostics.push(
      diagnostic(
        "H2VRC-RESOURCE-002",
        pointer,
        `Resource "${resourceId}" does not exist.`,
        nodeId
      )
    );
    return;
  }

  if (!allowedTypes.has(resource.type)) {
    diagnostics.push(
      diagnostic(
        "H2VRC-RESOURCE-003",
        pointer,
        `Resource "${resourceId}" has type "${resource.type}", expected ${[
          ...allowedTypes
        ].join(" or ")}.`,
        nodeId
      )
    );
  }
}

function validateStyle(
  style,
  pointer,
  resources,
  diagnostics,
  nodeId
) {
  if (style?.text?.font) {
    validateResourceReference(
      style.text.font,
      `${pointer}/text/font`,
      new Set(["font"]),
      resources,
      diagnostics,
      nodeId
    );
  }

  walkObject(style, pointer, (value, valuePointer) => {
    if (
      ["linear-gradient", "radial-gradient", "conic-gradient"].includes(
        value.type
      ) &&
      Array.isArray(value.stops)
    ) {
      for (let index = 1; index < value.stops.length; index += 1) {
        if (value.stops[index].position < value.stops[index - 1].position) {
          diagnostics.push(
            diagnostic(
              "H2VRC-PAINT-001",
              `${valuePointer}/stops/${index}/position`,
              "Gradient stop positions must be in non-decreasing order.",
              nodeId
            )
          );
        }
      }
    }

    if (value.type === "image" && value.resource) {
      validateResourceReference(
        value.resource,
        `${valuePointer}/resource`,
        new Set(["image", "sprite"]),
        resources,
        diagnostics,
        nodeId
      );
    }
  });
}

function validateExtensions(document, supportedExtensions, diagnostics) {
  const used = new Set(document.extensionsUsed ?? []);
  const required = new Set(document.extensionsRequired ?? []);

  for (const extension of required) {
    if (!used.has(extension)) {
      diagnostics.push(
        diagnostic(
          "H2VRC-EXTENSION-001",
          "/extensionsRequired",
          `Required extension "${extension}" is not listed in extensionsUsed.`
        )
      );
    }
    if (!supportedExtensions.has(extension)) {
      diagnostics.push(
        diagnostic(
          "H2VRC-EXTENSION-002",
          "/extensionsRequired",
          `Required extension "${extension}" is not supported by this validator profile.`
        )
      );
    }
  }

  walkObject(document, "", (value, pointer) => {
    if (!value.extensions || typeof value.extensions !== "object") {
      return;
    }
    for (const extension of Object.keys(value.extensions)) {
      if (!used.has(extension)) {
        diagnostics.push(
          diagnostic(
            "H2VRC-EXTENSION-003",
            `${pointer}/extensions/${escapePointer(extension)}`,
            `Extension "${extension}" is used but not listed in extensionsUsed.`
          )
        );
      }
    }
  });
}

function semanticDiagnostics(document, options) {
  const diagnostics = [];
  const supportedExtensions = new Set(options.supportedExtensions ?? []);
  const supportedVersion =
    options.supportedVersion ?? DEFAULT_SUPPORTED_VERSION;

  if (document.asset.version !== supportedVersion) {
    diagnostics.push(
      diagnostic(
        "H2VRC-VERSION-002",
        "/asset/version",
        `UDOM ${document.asset.version} is not supported by the ${supportedVersion} reference validator.`
      )
    );
  }

  if (
    document.asset.minVersion &&
    compareVersions(document.asset.minVersion, document.asset.version) > 0
  ) {
    diagnostics.push(
      diagnostic(
        "H2VRC-VERSION-001",
        "/asset/minVersion",
        `minVersion ${document.asset.minVersion} cannot be greater than version ${document.asset.version}.`
      )
    );
  }

  validateExtensions(document, supportedExtensions, diagnostics);

  const styleItems = (document.styles ?? []).map((style, index) => ({
    ...style,
    pointer: `/styles/${index}`
  }));
  const resourceItems = (document.resources ?? []).map((resource, index) => ({
    ...resource,
    pointer: `/resources/${index}`
  }));
  const nodeItems = collectNodes(document.root).map(({ node, pointer }) => ({
    id: node.id,
    pointer
  }));

  const styles = validateUniqueIds(
    styleItems,
    "style",
    "H2VRC-STYLE-001",
    diagnostics
  );
  const resourceIds = validateUniqueIds(
    resourceItems,
    "resource",
    "H2VRC-RESOURCE-001",
    diagnostics
  );
  validateUniqueIds(
    nodeItems,
    "node",
    "H2VRC-NODE-001",
    diagnostics
  );

  const resources = new Map();
  for (const resource of resourceItems) {
    if (resource.id && resourceIds.has(resource.id) && !resources.has(resource.id)) {
      resources.set(resource.id, resource);
    }
  }

  for (const style of styleItems) {
    validateStyle(
      style.style,
      `${style.pointer}/style`,
      resources,
      diagnostics
    );
  }

  for (const { node, pointer } of collectNodes(document.root)) {
    const nodeKind = node.type === "text" ? "text" : node.name;

    for (const [index, styleRef] of (node.styleRefs ?? []).entries()) {
      if (!styles.has(styleRef)) {
        diagnostics.push(
          diagnostic(
            "H2VRC-STYLE-002",
            `${pointer}/styleRefs/${index}`,
            `Style "${styleRef}" does not exist.`,
            node.id
          )
        );
      }
    }

    if (node.style) {
      validateStyle(
        node.style,
        `${pointer}/style`,
        resources,
        diagnostics,
        node.id
      );
    }

    if (node.type === "element" && node.name === "image") {
      validateResourceReference(
        node.properties.resource,
        `${pointer}/properties/resource`,
        new Set(["image", "sprite"]),
        resources,
        diagnostics,
        node.id
      );
    }

    for (const slot of Object.keys(node.bind ?? {})) {
      if (!BINDING_SLOTS[nodeKind]?.has(slot)) {
        diagnostics.push(
          diagnostic(
            "H2VRC-BINDING-001",
            `${pointer}/bind/${escapePointer(slot)}`,
            `Binding slot "${slot}" is not valid for ${nodeKind}.`,
            node.id
          )
        );
      }
    }

    for (const event of Object.keys(node.on ?? {})) {
      if (!EVENTS[nodeKind]?.has(event)) {
        diagnostics.push(
          diagnostic(
            "H2VRC-EVENT-001",
            `${pointer}/on/${escapePointer(event)}`,
            `Event "${event}" is not emitted by ${nodeKind}.`,
            node.id
          )
        );
      }
    }

    if (node.type === "element" && node.name === "slider") {
      const minimum = node.properties?.min ?? 0;
      const maximum = node.properties?.max ?? 1;
      const value = node.properties?.value ?? minimum;
      if (maximum <= minimum) {
        diagnostics.push(
          diagnostic(
            "H2VRC-CONTROL-001",
            `${pointer}/properties/max`,
            `Slider max (${maximum}) must be greater than min (${minimum}).`,
            node.id
          )
        );
      } else if (value < minimum || value > maximum) {
        diagnostics.push(
          diagnostic(
            "H2VRC-CONTROL-002",
            `${pointer}/properties/value`,
            `Slider value (${value}) must be between min (${minimum}) and max (${maximum}).`,
            node.id
          )
        );
      }
    }
  }

  return diagnostics;
}

/**
 * Validate a parsed UDOM document.
 *
 * Schema errors are returned first. Semantic checks only run after the
 * document passes the structural schema so callers never receive misleading
 * cross-reference diagnostics for malformed input.
 */
export function validateUdom(document, options = {}) {
  if (!validateSchema(document)) {
    return {
      valid: false,
      diagnostics: validateSchema.errors.map((error) =>
        diagnostic(
          "H2VRC-SCHEMA-001",
          error.instancePath,
          `${error.message}${error.params?.additionalProperty ? `: ${error.params.additionalProperty}` : ""}`
        )
      )
    };
  }

  const diagnostics = semanticDiagnostics(document, options);
  return {
    valid: diagnostics.length === 0,
    diagnostics
  };
}

export { schema };
