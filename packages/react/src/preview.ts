import { validateUdom } from "@html2vrc/udom";
import type {
  UdomBackground,
  UdomDiagnostic,
  UdomDocument,
  UdomElementNode,
  UdomGradientStop,
  UdomLength,
  UdomNode,
  UdomPoint,
  UdomResource,
  UdomStyle
} from "./types.js";

export interface UdomPreviewOptions {
  title?: string;
  liveReloadPath?: string;
}

export class UdomPreviewError extends Error {
  readonly diagnostics: readonly UdomDiagnostic[];

  constructor(message: string, diagnostics: readonly UdomDiagnostic[] = []) {
    super(message);
    this.name = "UdomPreviewError";
    this.diagnostics = diagnostics;
  }
}

interface PreviewContext {
  resources: ReadonlyMap<string, UdomResource>;
  styles: ReadonlyMap<string, UdomStyle>;
}

type JsonObject = Record<string, unknown>;

function escapeHtml(value: string): string {
  return value
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;");
}

function escapeAttribute(value: string): string {
  return escapeHtml(value)
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
}

function escapeCssString(value: string): string {
  return value
    .replaceAll("\\", "\\\\")
    .replaceAll('"', '\\"')
    .replaceAll("<", "\\3C ")
    .replaceAll(">", "\\3E ")
    .replaceAll("\n", "\\A ")
    .replaceAll("\r", "");
}

function escapeInlineScript(value: string): string {
  return value
    .replaceAll("<", "\\u003c")
    .replaceAll("\u2028", "\\u2028")
    .replaceAll("\u2029", "\\u2029");
}

function isObject(value: unknown): value is JsonObject {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function mergeValue(base: unknown, overlay: unknown): unknown {
  if (overlay === undefined) {
    return base;
  }
  if (!isObject(base) || !isObject(overlay)) {
    return overlay;
  }

  const result: JsonObject = { ...base };
  for (const [key, value] of Object.entries(overlay)) {
    result[key] = mergeValue(result[key], value);
  }
  return result;
}

function resolvedStyle(node: UdomNode, context: PreviewContext): UdomStyle {
  let style: UdomStyle = {};
  for (const reference of node.styleRefs ?? []) {
    style = mergeValue(style, context.styles.get(reference)) as UdomStyle;
  }
  return mergeValue(style, node.style) as UdomStyle;
}

function number(value: number): string {
  return Object.is(value, -0) ? "0" : String(value);
}

function length(value: UdomLength | undefined, fallback?: UdomLength): string | undefined {
  const result = value ?? fallback;
  if (result === undefined) {
    return undefined;
  }
  return typeof result === "number" ? `${number(result)}px` : result;
}

function coordinate(value: UdomLength | undefined, fallback: UdomLength = 0): string {
  return length(value === "auto" ? fallback : value, fallback) as string;
}

function cssDeclaration(name: string, value: string | undefined): string {
  return value === undefined ? "" : `${name}:${value}`;
}

function flexAlignment(value: string): string {
  return value === "start" || value === "end" ? `flex-${value}` : value;
}

function textAlignment(value: "start" | "center" | "end" | "justify"): string {
  if (value === "start" || value === "end") {
    return value;
  }
  return value;
}

function safeResourceUri(resource: UdomResource | undefined): string | undefined {
  if (!resource) {
    return undefined;
  }
  const uri = resource.uri.trim();
  if (/^(?:javascript|vbscript|file):/iu.test(uri)) {
    return undefined;
  }
  if (/^data:/iu.test(uri)) {
    const allowedType = resource.type === "font" ? "font" : "image";
    if (!new RegExp(`^data:${allowedType}/`, "iu").test(uri)) {
      return undefined;
    }
  }
  return uri;
}

function cssUrl(resource: UdomResource | undefined): string | undefined {
  const uri = safeResourceUri(resource);
  return uri === undefined ? undefined : `url("${escapeCssString(uri)}")`;
}

function gradientStops(stops: UdomGradientStop[]): string {
  return stops
    .map((stop) => `${stop.color} ${number(stop.position * 100)}%`)
    .join(",");
}

function point(pointValue: UdomPoint | undefined): string {
  return `${length(pointValue?.x, "50%")} ${length(pointValue?.y, "50%")}`;
}

function backgroundCss(
  background: UdomBackground,
  resources: ReadonlyMap<string, UdomResource>
): string | undefined {
  switch (background.type) {
    case "color":
      return `linear-gradient(${background.color},${background.color})`;
    case "linear-gradient":
      return `linear-gradient(${number(background.angle ?? 180)}deg,${gradientStops(background.stops)})`;
    case "radial-gradient": {
      const center = point(background.center);
      const radiusX = length(background.radius?.x, "50%");
      const radiusY = length(background.radius?.y, "50%");
      return `radial-gradient(ellipse ${radiusX} ${radiusY} at ${center},${gradientStops(background.stops)})`;
    }
    case "conic-gradient":
      return `conic-gradient(from ${number(background.angle ?? 0)}deg at ${point(background.center)},${gradientStops(background.stops)})`;
    case "image": {
      const url = cssUrl(resources.get(background.resource));
      if (url === undefined) {
        return undefined;
      }
      const fit = background.fit ?? "contain";
      const size = fit === "fill" ? "100% 100%" : fit === "none" ? "auto" : fit;
      const repeats = {
        none: "no-repeat",
        x: "repeat-x",
        y: "repeat-y",
        both: "repeat"
      } as const;
      return `${url} ${point(background.position)} / ${size} ${repeats[background.repeat ?? "none"]}`;
    }
  }
}

function layoutCss(style: UdomStyle): string[] {
  const layout = style.layout;
  if (!layout) {
    return [];
  }
  if (layout.mode === "none") {
    return ["display:none"];
  }

  const result: string[] = [];
  if (layout.mode === "flex") {
    result.push("display:flex");
  }
  result.push(cssDeclaration("--h2vrc-x", layout.x === undefined ? undefined : coordinate(layout.x)));
  result.push(cssDeclaration("--h2vrc-y", layout.y === undefined ? undefined : coordinate(layout.y)));
  result.push(cssDeclaration("width", length(layout.width)));
  result.push(cssDeclaration("height", length(layout.height)));
  result.push(cssDeclaration("min-width", length(layout.minWidth)));
  result.push(cssDeclaration("min-height", length(layout.minHeight)));
  result.push(cssDeclaration("max-width", length(layout.maxWidth)));
  result.push(cssDeclaration("max-height", length(layout.maxHeight)));

  if (layout.margin) {
    result.push(
      `margin:${length(layout.margin.top, 0)} ${length(layout.margin.right, 0)} ${length(layout.margin.bottom, 0)} ${length(layout.margin.left, 0)}`
    );
  }
  if (layout.padding) {
    result.push(
      `padding:${length(layout.padding.top, 0)} ${length(layout.padding.right, 0)} ${length(layout.padding.bottom, 0)} ${length(layout.padding.left, 0)}`
    );
  }
  result.push(cssDeclaration("overflow-x", layout.overflowX));
  result.push(cssDeclaration("overflow-y", layout.overflowY));
  result.push(cssDeclaration("z-index", layout.zIndex === undefined ? undefined : number(layout.zIndex)));
  result.push(cssDeclaration("aspect-ratio", layout.aspectRatio === undefined ? undefined : number(layout.aspectRatio)));

  if (layout.flex) {
    const flex = layout.flex;
    result.push(cssDeclaration("flex-direction", flex.direction));
    result.push(cssDeclaration("flex-wrap", flex.wrap));
    result.push(cssDeclaration("justify-content", flex.justify === undefined ? undefined : flexAlignment(flex.justify)));
    result.push(cssDeclaration("align-items", flex.alignItems === undefined ? undefined : flexAlignment(flex.alignItems)));
    result.push(cssDeclaration("align-content", flex.alignContent === undefined ? undefined : flexAlignment(flex.alignContent)));
    result.push(cssDeclaration("row-gap", length(flex.rowGap)));
    result.push(cssDeclaration("column-gap", length(flex.columnGap)));
  }
  if (layout.flexItem) {
    const item = layout.flexItem;
    result.push(cssDeclaration("flex-grow", item.grow === undefined ? undefined : number(item.grow)));
    result.push(cssDeclaration("flex-shrink", item.shrink === undefined ? undefined : number(item.shrink)));
    result.push(cssDeclaration("flex-basis", length(item.basis)));
    result.push(cssDeclaration("align-self", item.alignSelf === undefined || item.alignSelf === "auto" ? item.alignSelf : flexAlignment(item.alignSelf)));
    result.push(cssDeclaration("order", item.order === undefined ? undefined : number(item.order)));
  }
  return result;
}

function paintCss(style: UdomStyle, context: PreviewContext): string[] {
  const paint = style.paint;
  if (!paint) {
    return [];
  }
  const result: string[] = [];
  if (paint.visible === false) {
    result.push("visibility:hidden");
  }
  result.push(cssDeclaration("opacity", paint.opacity === undefined ? undefined : number(paint.opacity)));

  if (paint.backgrounds) {
    const backgrounds = paint.backgrounds
      .map((background) => backgroundCss(background, context.resources))
      .filter((background): background is string => background !== undefined)
      .reverse();
    if (backgrounds.length > 0) {
      result.push(`background:${backgrounds.join(",")}`);
    }
  }

  for (const edge of ["top", "right", "bottom", "left"] as const) {
    const border = paint.border?.[edge];
    if (border) {
      result.push(
        `border-${edge}:${number(border.width ?? 0)}px ${border.style ?? "solid"} ${border.color ?? "#00000000"}`
      );
    }
  }
  if (paint.radius) {
    result.push(
      `border-radius:${length(paint.radius.topLeft, 0)} ${length(paint.radius.topRight, 0)} ${length(paint.radius.bottomRight, 0)} ${length(paint.radius.bottomLeft, 0)}`
    );
  }
  if (paint.shadows && paint.shadows.length > 0) {
    result.push(
      `box-shadow:${paint.shadows
        .map((shadow) => `${shadow.inset ? "inset " : ""}${number(shadow.offsetX ?? 0)}px ${number(shadow.offsetY ?? 0)}px ${number(shadow.blur ?? 0)}px ${number(shadow.spread ?? 0)}px ${shadow.color ?? "#00000080"}`)
        .reverse()
        .join(",")}`
    );
  }
  return result;
}

function textCss(style: UdomStyle, context: PreviewContext): string[] {
  const text = style.text;
  if (!text) {
    return [];
  }
  const result: string[] = [];
  const font = text.font === undefined ? undefined : context.resources.get(text.font);
  if (font) {
    result.push(`font-family:"udom-${escapeCssString(font.id)}"`);
  }
  result.push(cssDeclaration("font-size", text.fontSize === undefined ? undefined : `${number(text.fontSize)}px`));
  result.push(cssDeclaration("font-weight", text.fontWeight === undefined ? undefined : number(text.fontWeight)));
  result.push(cssDeclaration("font-style", text.fontStyle));
  result.push(cssDeclaration("color", text.color));
  result.push(cssDeclaration("line-height", text.lineHeight === undefined ? undefined : text.lineHeight === "normal" ? "normal" : `${number(text.lineHeight)}px`));
  result.push(cssDeclaration("letter-spacing", text.letterSpacing === undefined ? undefined : `${number(text.letterSpacing)}px`));
  result.push(cssDeclaration("text-align", text.align === undefined ? undefined : textAlignment(text.align)));

  if (text.verticalAlign) {
    result.push("display:flex");
    result.push(
      `align-items:${text.verticalAlign === "top" ? "flex-start" : text.verticalAlign === "bottom" ? "flex-end" : "center"}`
    );
  }
  if (text.preserveWhitespace) {
    result.push(`white-space:${text.wrap === "nowrap" ? "pre" : "pre-wrap"}`);
  } else if (text.wrap === "nowrap") {
    result.push("white-space:nowrap");
  }
  if (text.overflow === "visible") {
    result.push("overflow:visible");
  } else if (text.overflow === "clip") {
    result.push("overflow:hidden");
  } else if (text.overflow === "ellipsis") {
    result.push("overflow:hidden", "text-overflow:ellipsis");
    if (text.wrap !== "wrap") {
      result.push("white-space:nowrap");
    }
  }
  return result;
}

function transformCss(style: UdomStyle): string[] {
  const transform = style.transform;
  if (!transform) {
    return [];
  }
  const result: string[] = [];
  if (transform.origin) {
    result.push(`transform-origin:${point(transform.origin)}`);
  }
  if (transform.operations && transform.operations.length > 0) {
    const operations = transform.operations
      .map((operation) => {
        switch (operation.type) {
          case "translate":
            return `translate(${coordinate(operation.x)},${coordinate(operation.y)})`;
          case "rotate":
            return `rotate(${number(operation.degrees)}deg)`;
          case "scale":
            return `scale(${number(operation.x ?? 1)},${number(operation.y ?? 1)})`;
        }
      })
      .reverse();
    result.push(`transform:${operations.join(" ")}`);
  }
  return result;
}

function styleAttribute(node: UdomNode, context: PreviewContext): string {
  const style = resolvedStyle(node, context);
  const declarations = [
    ...layoutCss(style),
    ...paintCss(style, context),
    ...textCss(style, context),
    ...transformCss(style)
  ].filter((declaration) => declaration.length > 0);
  return declarations.length === 0
    ? ""
    : ` style="${escapeAttribute(`${declarations.join(";")};`)}"`;
}

function nodeClass(node: UdomNode, context: PreviewContext): string {
  const style = resolvedStyle(node, context);
  const layout = style.layout;
  const mode = layout?.mode ?? "absolute";
  const position = layout?.position ?? "flow";
  const kind = node.type === "text" ? "text" : node.name;
  const textClasses = node.type === "text"
    ? `${style.text?.verticalAlign === undefined ? "" : " h2vrc-text-vertical"}${style.text?.overflow === "ellipsis" ? ` h2vrc-text-ellipsis h2vrc-text-wrap-${style.text.wrap ?? "wrap"}` : ""}`
    : "";
  return `h2vrc-node h2vrc-${kind} h2vrc-mode-${mode} h2vrc-position-${position}${textClasses}`;
}

function metadataAttributes(node: UdomNode): string {
  const parts = [
    `id="${escapeAttribute(node.id)}"`,
    `data-udom-id="${escapeAttribute(node.id)}"`,
    `data-udom-type="${node.type === "text" ? "text" : escapeAttribute(node.name)}"`
  ];
  if (node.bind) {
    parts.push(`data-udom-bind="${escapeAttribute(JSON.stringify(node.bind))}"`);
  }
  if (node.type === "element" && node.on) {
    parts.push(`data-udom-on="${escapeAttribute(JSON.stringify(node.on))}"`);
  }
  return parts.join(" ");
}

function propertyString(properties: JsonObject, name: string, fallback = ""): string {
  const value = properties[name];
  return typeof value === "string" ? value : fallback;
}

function propertyNumber(properties: JsonObject, name: string, fallback: number): number {
  const value = properties[name];
  return typeof value === "number" ? value : fallback;
}

function propertyBoolean(properties: JsonObject, name: string, fallback = false): boolean {
  const value = properties[name];
  return typeof value === "boolean" ? value : fallback;
}

function booleanAttribute(name: string, enabled: boolean): string {
  return enabled ? ` ${name}` : "";
}

function renderChildren(node: UdomElementNode, context: PreviewContext): string {
  return (node.children ?? []).map((child) => renderNode(child, context)).join("");
}

function renderElement(node: UdomElementNode, context: PreviewContext): string {
  const properties = node.properties ?? {};
  const attributes = metadataAttributes(node);
  const style = styleAttribute(node, context);
  const children = renderChildren(node, context);
  const className = nodeClass(node, context);

  switch (node.name) {
    case "view":
      return `<div class="${className}" ${attributes}${style}>${children}</div>`;
    case "image": {
      const resourceId = propertyString(properties, "resource");
      const resource = context.resources.get(resourceId);
      const uri = safeResourceUri(resource);
      const fit = propertyString(properties, "fit", "contain");
      const positionValue = properties.position;
      const position = isObject(positionValue)
        ? `${length(positionValue.x as UdomLength | undefined, "50%")} ${length(positionValue.y as UdomLength | undefined, "50%")}`
        : "50% 50%";
      const imageStyle = `object-fit:${fit};object-position:${position};`;
      const nodeStyle = style === ""
        ? ` style="${escapeAttribute(imageStyle)}"`
        : style.replace(/ style="/u, ` style="${escapeAttribute(imageStyle)}`);
      const source = uri === undefined ? "" : escapeAttribute(uri);
      const missing = uri === undefined ? " data-preview-missing-resource" : "";
      return `<img class="${className}" ${attributes} src="${source}" alt=""${missing}${nodeStyle}>`;
    }
    case "button":
      return `<button type="button" class="${className}" ${attributes}${booleanAttribute("disabled", propertyBoolean(properties, "disabled"))}${style}>${children}</button>`;
    case "toggle":
      return `<label class="${className}" ${attributes}${style}><input type="checkbox"${booleanAttribute("checked", propertyBoolean(properties, "checked"))}${booleanAttribute("disabled", propertyBoolean(properties, "disabled"))}>${children}</label>`;
    case "slider": {
      const minimum = propertyNumber(properties, "min", 0);
      const maximum = propertyNumber(properties, "max", 1);
      const value = propertyNumber(properties, "value", minimum);
      const step = propertyNumber(properties, "step", 0);
      return `<label class="${className}" ${attributes}${style}><input type="range" min="${number(minimum)}" max="${number(maximum)}" value="${number(value)}" step="${step === 0 ? "any" : number(step)}"${booleanAttribute("disabled", propertyBoolean(properties, "disabled"))}>${children}</label>`;
    }
    case "text-input": {
      const value = escapeHtml(propertyString(properties, "value"));
      const placeholder = escapeAttribute(propertyString(properties, "placeholder"));
      const common = `class="h2vrc-native-text-input" placeholder="${placeholder}"${booleanAttribute("readonly", propertyBoolean(properties, "readOnly"))}${booleanAttribute("disabled", propertyBoolean(properties, "disabled"))}`;
      const input = propertyBoolean(properties, "multiline")
        ? `<textarea ${common}>${value}</textarea>`
        : `<input type="text" ${common} value="${escapeAttribute(propertyString(properties, "value"))}">`;
      return `<label class="${className}" ${attributes}${style}>${input}${children}</label>`;
    }
    case "scroll": {
      const axis = propertyString(properties, "axis", "vertical");
      const offsetValue = properties.initialOffset;
      const offset = isObject(offsetValue) ? offsetValue : {};
      const x = typeof offset.x === "number" ? offset.x : 0;
      const y = typeof offset.y === "number" ? offset.y : 0;
      return `<div class="${className}" ${attributes} data-scroll-axis="${escapeAttribute(axis)}" data-scroll-x="${number(x)}" data-scroll-y="${number(y)}"${style}>${children}</div>`;
    }
    case "embed": {
      const object = propertyString(properties, "object");
      const fallback = propertyString(properties, "fallbackLabel");
      return `<div class="${className}" ${attributes} data-udom-object="${escapeAttribute(object)}"${style}><span class="h2vrc-embed-label">${escapeHtml(fallback)}</span></div>`;
    }
  }
}

function renderNode(node: UdomNode, context: PreviewContext): string {
  if (node.type === "text") {
    return `<div class="${nodeClass(node, context)}" ${metadataAttributes(node)}${styleAttribute(node, context)}><span class="h2vrc-text-content">${escapeHtml(node.value)}</span></div>`;
  }
  return renderElement(node, context);
}

function fontFaces(context: PreviewContext): string {
  const faces: string[] = [];
  for (const resource of context.resources.values()) {
    if (resource.type !== "font") {
      continue;
    }
    const source = cssUrl(resource);
    if (source !== undefined) {
      faces.push(`@font-face{font-family:"udom-${escapeCssString(resource.id)}";src:${source};font-display:swap;}`);
    }
  }
  return faces.join("");
}

function runtimeScript(document: UdomDocument, liveReloadPath: string | undefined): string {
  const viewport = JSON.stringify({
    width: document.viewport.width,
    height: document.viewport.height,
    fit: document.viewport.fit ?? "contain"
  });
  const reloadPath = liveReloadPath === undefined ? "null" : JSON.stringify(liveReloadPath);
  return escapeInlineScript(`(()=>{const viewport=${viewport};const canvas=document.querySelector(".h2vrc-viewport");const updateTextOverflow=()=>{for(const element of document.querySelectorAll(".h2vrc-text-ellipsis")){const content=element.querySelector(".h2vrc-text-content");if(!content)continue;element.removeAttribute("data-preview-overflowing");const overflowing=content.scrollWidth>content.clientWidth||content.scrollHeight>element.clientHeight;element.toggleAttribute("data-preview-overflowing",overflowing);if(element.classList.contains("h2vrc-text-wrap-wrap")){const style=getComputedStyle(content);const fontSize=parseFloat(style.fontSize)||16;const parsedLineHeight=parseFloat(style.lineHeight);const lineHeight=Number.isFinite(parsedLineHeight)?parsedLineHeight:fontSize*1.2;element.style.setProperty("--h2vrc-line-clamp",String(Math.max(1,Math.floor(element.clientHeight/lineHeight))))}}};const resize=()=>{const x=window.innerWidth/viewport.width;const y=window.innerHeight/viewport.height;let sx=1;let sy=1;if(viewport.fit==="contain"){sx=sy=Math.min(x,y)}else if(viewport.fit==="cover"){sx=sy=Math.max(x,y)}else if(viewport.fit==="stretch"){sx=x;sy=y}canvas.style.transform="scale("+sx+","+sy+")";updateTextOverflow()};resize();window.addEventListener("resize",resize);document.fonts?.ready.then(updateTextOverflow);for(const element of document.querySelectorAll(".h2vrc-scroll")){element.scrollLeft=Number(element.dataset.scrollX||0);element.scrollTop=Number(element.dataset.scrollY||0)}const liveReloadPath=${reloadPath};if(liveReloadPath){const events=new EventSource(liveReloadPath);events.addEventListener("reload",()=>location.reload())}})();`);
}

/**
 * Render a validated canonical UDOM 0.1 document as a deterministic,
 * standalone browser preview. Symbolic bindings and events are exposed only
 * as data attributes; the preview never executes them.
 */
export function renderPreviewToHTML(
  document: UdomDocument,
  options: UdomPreviewOptions = {}
): string {
  const validation = validateUdom(document);
  if (!validation.valid) {
    throw new UdomPreviewError(
      `UDOM preview validation failed with ${validation.diagnostics.length} diagnostic(s).`,
      validation.diagnostics
    );
  }

  const context: PreviewContext = {
    resources: new Map((document.resources ?? []).map((resource) => [resource.id, resource])),
    styles: new Map((document.styles ?? []).map((definition) => [definition.id, definition.style]))
  };
  const title = options.title ?? "HTML2VRC Preview";
  const body = renderNode(document.root, context);
  const style = `${fontFaces(context)}
html,body{width:100%;height:100%;margin:0;overflow:hidden;background:#17191f;color:#fff;font-family:system-ui,sans-serif}.h2vrc-stage{position:relative;width:100%;height:100%;display:flex;align-items:center;justify-content:center;background-color:#20232b;background-image:linear-gradient(45deg,#292d37 25%,transparent 25%),linear-gradient(-45deg,#292d37 25%,transparent 25%),linear-gradient(45deg,transparent 75%,#292d37 75%),linear-gradient(-45deg,transparent 75%,#292d37 75%);background-size:24px 24px;background-position:0 0,0 12px,12px -12px,-12px 0}.h2vrc-viewport{position:relative;flex:none;width:${number(document.viewport.width)}px;height:${number(document.viewport.height)}px;overflow:visible;transform-origin:center center;background:#0000}.h2vrc-node{position:relative;box-sizing:border-box;min-width:0;min-height:0}.h2vrc-mode-none{display:none!important}.h2vrc-mode-absolute>.h2vrc-node,.h2vrc-position-absolute{position:absolute;left:var(--h2vrc-x,0px);top:var(--h2vrc-y,0px)}.h2vrc-text{overflow:hidden;color:#000000FF;font-size:16px;font-style:normal;font-weight:400;line-height:normal;letter-spacing:0;text-align:start;white-space:normal}.h2vrc-text-content{display:block;min-width:0;max-width:100%}.h2vrc-text-vertical>.h2vrc-text-content{width:100%}.h2vrc-text-ellipsis>.h2vrc-text-content{overflow:hidden;text-overflow:ellipsis}.h2vrc-text-ellipsis[data-preview-overflowing]>.h2vrc-text-content{text-align:start}.h2vrc-text-ellipsis.h2vrc-text-wrap-wrap[data-preview-overflowing]>.h2vrc-text-content{display:-webkit-box;-webkit-box-orient:vertical;-webkit-line-clamp:var(--h2vrc-line-clamp,1)}.h2vrc-image{display:block}.h2vrc-button,.h2vrc-toggle,.h2vrc-slider,.h2vrc-text-input{font:inherit;color:inherit}.h2vrc-toggle,.h2vrc-slider,.h2vrc-text-input{display:flex;align-items:center}.h2vrc-native-text-input{box-sizing:border-box;min-width:0;flex:1 1 auto;font:inherit;color:inherit}.h2vrc-scroll[data-scroll-axis="vertical"]{overflow-x:hidden;overflow-y:scroll}.h2vrc-scroll[data-scroll-axis="horizontal"]{overflow-x:scroll;overflow-y:hidden}.h2vrc-scroll[data-scroll-axis="both"]{overflow:scroll}.h2vrc-embed{display:flex;align-items:center;justify-content:center;background:repeating-linear-gradient(135deg,#6b728033 0 8px,#11182733 8px 16px);outline:1px dashed #9ca3af}.h2vrc-embed-label{padding:4px 8px;color:#d1d5db;font:12px/1.4 ui-monospace,monospace}`;

  return `<!doctype html>\n<html lang="en">\n<head>\n<meta charset="utf-8">\n<meta name="viewport" content="width=device-width,initial-scale=1">\n<title>${escapeHtml(title)}</title>\n<style>${style}</style>\n</head>\n<body>\n<div class="h2vrc-stage"><main class="h2vrc-viewport" data-udom-width="${number(document.viewport.width)}" data-udom-height="${number(document.viewport.height)}" data-udom-fit="${escapeAttribute(document.viewport.fit ?? "contain")}">${body}</main></div>\n<script>${runtimeScript(document, options.liveReloadPath)}</script>\n</body>\n</html>\n`;
}
