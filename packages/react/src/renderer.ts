import { Children, Fragment, isValidElement } from "react";
import type { ReactElement, ReactNode } from "react";
import { validateUdom } from "@html2vrc/udom";
import { getPrimitiveName } from "./primitives.js";
import type {
  ButtonProps,
  CommonPrimitiveProps,
  ImageProps,
  RenderToUdomOptions,
  TextProps,
  UdomDiagnostic,
  UdomDocument,
  UdomElementNode,
  UdomNode,
  UdomNodeBase,
  UdomTextNode,
  ViewProps
} from "./types.js";

const GENERATOR = "@html2vrc/react 0.1.0";

export class UdomRenderError extends Error {
  readonly diagnostics: readonly UdomDiagnostic[];

  constructor(message: string, diagnostics: readonly UdomDiagnostic[] = []) {
    super(message);
    this.name = "UdomRenderError";
    this.diagnostics = diagnostics;
  }
}

function pathSegment(element: ReactNode, index: number): string {
  if (isValidElement(element) && element.key !== null) {
    return String(element.key)
      .replace(/^\.\$?/, "")
      .replace(/[^A-Za-z0-9._-]+/g, "-")
      .replace(/^-+|-+$/g, "") || String(index);
  }
  return String(index);
}

function generatedId(name: string, path: readonly string[]): string {
  return `${name}-${path.join("-")}`;
}

function commonNodeProps(
  props: CommonPrimitiveProps,
  name: string,
  path: readonly string[]
): UdomNodeBase {
  return {
    id: props.id ?? generatedId(name, path),
    ...(props.styleRefs ? { styleRefs: props.styleRefs } : {}),
    ...(props.style ? { style: props.style } : {}),
    ...(props.bind ? { bind: props.bind } : {}),
    ...(props.extensions ? { extensions: props.extensions } : {}),
    ...(props.extras !== undefined ? { extras: props.extras } : {})
  };
}

function childArray(children: ReactNode): ReactNode[] {
  const result: ReactNode[] = [];
  Children.forEach(children, (child) => {
    if (child !== null && child !== undefined && typeof child !== "boolean") {
      result.push(child);
    }
  });
  return result;
}

function renderChildren(
  children: ReactNode,
  parentPath: readonly string[]
): UdomNode[] {
  return childArray(children).flatMap((child, index) =>
    renderReactNode(child, [...parentPath, pathSegment(child, index)])
  );
}

function textValue(props: TextProps): string {
  if (props.value !== undefined && props.children !== undefined) {
    throw new UdomRenderError(
      "Text accepts either value or children, but not both."
    );
  }

  if (props.value !== undefined) {
    return String(props.value);
  }

  let value = "";
  Children.forEach(props.children, (child) => {
    if (
      typeof child !== "string" &&
      typeof child !== "number" &&
      child !== null &&
      child !== undefined &&
      typeof child !== "boolean"
    ) {
      throw new UdomRenderError(
        "Text children must contain only strings and numbers."
      );
    }
    if (typeof child === "string" || typeof child === "number") {
      value += String(child);
    }
  });
  return value;
}

function renderPrimitive(
  element: ReactElement,
  name: "view" | "text" | "image" | "button",
  path: readonly string[]
): UdomNode {
  if (name === "text") {
    const props = element.props as TextProps;
    const node: UdomTextNode = {
      type: "text",
      ...commonNodeProps(props, name, path),
      value: textValue(props)
    };
    return node;
  }

  if (name === "image") {
    const props = element.props as ImageProps;
    const node: UdomElementNode = {
      type: "element",
      ...commonNodeProps(props, name, path),
      name: "image",
      properties: {
        resource: props.src,
        ...(props.fit ? { fit: props.fit } : {}),
        ...(props.position ? { position: props.position } : {})
      }
    };
    return node;
  }

  if (name === "button") {
    const props = element.props as ButtonProps;
    const children = renderChildren(props.children, path);
    const node: UdomElementNode = {
      type: "element",
      ...commonNodeProps(props, name, path),
      name: "button",
      ...(props.disabled !== undefined
        ? { properties: { disabled: props.disabled } }
        : {}),
      ...(props.on ? { on: props.on } : {}),
      ...(children.length > 0 ? { children } : {})
    };
    return node;
  }

  const props = element.props as ViewProps;
  const children = renderChildren(props.children, path);
  const node: UdomElementNode = {
    type: "element",
    ...commonNodeProps(props, name, path),
    name: "view",
    ...(children.length > 0 ? { children } : {})
  };
  return node;
}

function renderReactNode(
  input: ReactNode,
  path: readonly string[]
): UdomNode[] {
  if (input === null || input === undefined || typeof input === "boolean") {
    return [];
  }

  if (typeof input === "string" || typeof input === "number") {
    throw new UdomRenderError(
      "Bare text is not supported. Wrap strings and numbers in <Text>."
    );
  }

  if (!isValidElement(input)) {
    throw new UdomRenderError(
      "Unsupported React child. Use @html2vrc/react primitives and fragments."
    );
  }

  if (input.type === Fragment) {
    const props = input.props as { children?: ReactNode };
    return renderChildren(props.children, path);
  }

  const primitiveName = getPrimitiveName(input.type);
  if (!primitiveName) {
    const label =
      typeof input.type === "string"
        ? `<${input.type}>`
        : (input.type as { displayName?: string; name?: string }).displayName ??
          (input.type as { name?: string }).name ??
          "unknown component";
    throw new UdomRenderError(
      `Unsupported React element ${label}. The 0.1 exporter accepts only View, Text, Image, Button, and Fragment.`
    );
  }

  return [renderPrimitive(input, primitiveName, path)];
}

export function renderToUDOM(
  element: ReactNode,
  options: RenderToUdomOptions
): UdomDocument {
  const roots = renderReactNode(element, ["0"]);
  if (roots.length !== 1) {
    throw new UdomRenderError(
      `A UDOM document requires exactly one root node; received ${roots.length}.`
    );
  }

  const root = roots[0];
  if (!root) {
    throw new UdomRenderError("A UDOM document requires one root node.");
  }

  const document: UdomDocument = {
    asset: {
      version: "0.1",
      generator: GENERATOR,
      ...options.asset
    },
    viewport: options.viewport,
    root,
    ...(options.styles ? { styles: options.styles } : {}),
    ...(options.resources ? { resources: options.resources } : {}),
    ...(options.extensionsUsed
      ? { extensionsUsed: options.extensionsUsed }
      : {}),
    ...(options.extensionsRequired
      ? { extensionsRequired: options.extensionsRequired }
      : {}),
    ...(options.extensions ? { extensions: options.extensions } : {}),
    ...(options.extras !== undefined ? { extras: options.extras } : {})
  };

  const result = validateUdom(
    document,
    options.supportedExtensions
      ? { supportedExtensions: options.supportedExtensions }
      : {}
  );
  if (!result.valid) {
    throw new UdomRenderError(
      `Generated UDOM failed conformance validation with ${result.diagnostics.length} error(s).`,
      result.diagnostics
    );
  }

  return document;
}
