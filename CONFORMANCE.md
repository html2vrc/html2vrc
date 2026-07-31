# UDOM 0.1 Conformance

UDOM implementations share three conformance assets.

1. `SPECIFICATION.md` defines UDOM meaning.
2. `schemas/udom-0.1.schema.json` defines the structural JSON contract.
3. `fixtures/manifest.json` defines portable valid and invalid examples.

The JavaScript validator in `src/validator.mjs` is the reference implementation. Other languages do not need to reproduce its source, but they should produce the same valid/invalid result for every fixture.

## Validation order

Implementations should validate in this order:

1. Parse UTF-8 JSON.
2. Validate the JSON Schema.
3. Check version and extension compatibility.
4. Check ID uniqueness and references.
5. Check node-specific Binding, event and value constraints.

Semantic validation should only run after structural validation succeeds. This prevents misleading reference errors from malformed documents.

## Diagnostic shape

The reference validator returns:

```json
{
  "valid": false,
  "diagnostics": [
    {
      "code": "H2VRC-RESOURCE-002",
      "severity": "error",
      "pointer": "/root/properties/resource",
      "nodeId": "profile-image",
      "message": "Resource \"image.missing\" does not exist."
    }
  ]
}
```

`pointer` is an RFC 6901 JSON Pointer. `nodeId` is included when the error belongs to a node.

## Stable diagnostic families

| Family | Meaning |
|---|---|
| `H2VRC-JSON-*` | JSON parsing |
| `H2VRC-SCHEMA-*` | Structural schema |
| `H2VRC-VERSION-*` | UDOM version compatibility |
| `H2VRC-EXTENSION-*` | Extension declaration or support |
| `H2VRC-NODE-*` | Node identity or tree rules |
| `H2VRC-STYLE-*` | Style identity or reference |
| `H2VRC-RESOURCE-*` | Resource identity, reference or type |
| `H2VRC-PAINT-*` | Paint and gradient constraints |
| `H2VRC-BINDING-*` | Binding slot constraints |
| `H2VRC-EVENT-*` | Event constraints |
| `H2VRC-CONTROL-*` | Control value constraints |

Codes may be added within a family during the 0.x draft period. Existing fixture codes should not be reassigned to a different meaning.

## Adding a fixture

- Put accepted documents in `fixtures/valid`.
- Put rejected documents in `fixtures/invalid`.
- Add every fixture to `fixtures/manifest.json`.
- Invalid fixtures should isolate one primary rule and declare its expected diagnostic code.
- Run `npm test` before changing the specification or schema.
