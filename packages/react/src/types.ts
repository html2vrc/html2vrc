import type { ReactNode } from "react";

export type UdomLength = number | `${number}%` | "auto";
export type UdomNonNegativeLength = number | `${number}%` | "auto";
export type UdomColor = `#${string}`;

export interface UdomEdges {
  top?: UdomLength;
  right?: UdomLength;
  bottom?: UdomLength;
  left?: UdomLength;
}

export interface UdomNonNegativeEdges {
  top?: UdomNonNegativeLength;
  right?: UdomNonNegativeLength;
  bottom?: UdomNonNegativeLength;
  left?: UdomNonNegativeLength;
}

export interface UdomPoint<T = UdomLength> {
  x: T;
  y: T;
}

export interface UdomFlexContainer {
  direction?: "row" | "row-reverse" | "column" | "column-reverse";
  wrap?: "nowrap" | "wrap" | "wrap-reverse";
  justify?:
    | "start"
    | "center"
    | "end"
    | "space-between"
    | "space-around"
    | "space-evenly";
  alignItems?: "start" | "center" | "end" | "stretch";
  alignContent?:
    | "start"
    | "center"
    | "end"
    | "stretch"
    | "space-between"
    | "space-around";
  rowGap?: UdomNonNegativeLength;
  columnGap?: UdomNonNegativeLength;
}

export interface UdomFlexItem {
  grow?: number;
  shrink?: number;
  basis?: UdomNonNegativeLength;
  alignSelf?: "auto" | "start" | "center" | "end" | "stretch";
  order?: number;
}

export interface UdomLayoutStyle {
  mode?: "absolute" | "flex" | "none";
  position?: "flow" | "absolute";
  x?: UdomLength;
  y?: UdomLength;
  width?: UdomNonNegativeLength;
  height?: UdomNonNegativeLength;
  minWidth?: UdomNonNegativeLength;
  minHeight?: UdomNonNegativeLength;
  maxWidth?: UdomNonNegativeLength;
  maxHeight?: UdomNonNegativeLength;
  margin?: UdomEdges;
  padding?: UdomNonNegativeEdges;
  overflowX?: "visible" | "hidden" | "scroll";
  overflowY?: "visible" | "hidden" | "scroll";
  zIndex?: number;
  aspectRatio?: number;
  flex?: UdomFlexContainer;
  flexItem?: UdomFlexItem;
}

export interface UdomGradientStop {
  position: number;
  color: UdomColor;
}

export type UdomBackground =
  | {
      type: "color";
      color: UdomColor;
    }
  | {
      type: "linear-gradient";
      angle?: number;
      stops: UdomGradientStop[];
    }
  | {
      type: "radial-gradient";
      center?: UdomPoint;
      radius?: UdomPoint;
      stops: UdomGradientStop[];
    }
  | {
      type: "conic-gradient";
      center?: UdomPoint;
      angle?: number;
      stops: UdomGradientStop[];
    }
  | {
      type: "image";
      resource: string;
      fit?: "fill" | "contain" | "cover" | "none";
      position?: UdomPoint;
      repeat?: "none" | "x" | "y" | "both";
    };

export interface UdomBorderEdge {
  width?: number;
  color?: UdomColor;
  style?: "solid";
}

export interface UdomPaintStyle {
  visible?: boolean;
  opacity?: number;
  backgrounds?: UdomBackground[];
  border?: {
    top?: UdomBorderEdge;
    right?: UdomBorderEdge;
    bottom?: UdomBorderEdge;
    left?: UdomBorderEdge;
  };
  radius?: {
    topLeft?: UdomNonNegativeLength;
    topRight?: UdomNonNegativeLength;
    bottomRight?: UdomNonNegativeLength;
    bottomLeft?: UdomNonNegativeLength;
  };
  shadows?: Array<{
    offsetX?: number;
    offsetY?: number;
    blur?: number;
    spread?: number;
    color?: UdomColor;
    inset?: boolean;
  }>;
}

export interface UdomTextStyle {
  font?: string;
  fontSize?: number;
  fontWeight?: number;
  fontStyle?: "normal" | "italic";
  color?: UdomColor;
  lineHeight?: number | "normal";
  letterSpacing?: number;
  align?: "start" | "center" | "end" | "justify";
  verticalAlign?: "top" | "middle" | "bottom";
  wrap?: "wrap" | "nowrap";
  overflow?: "visible" | "clip" | "ellipsis";
  preserveWhitespace?: boolean;
}

export type UdomTransformOperation =
  | {
      type: "translate";
      x?: UdomLength;
      y?: UdomLength;
    }
  | {
      type: "rotate";
      degrees: number;
    }
  | {
      type: "scale";
      x?: number;
      y?: number;
    };

export interface UdomStyle {
  layout?: UdomLayoutStyle;
  paint?: UdomPaintStyle;
  text?: UdomTextStyle;
  transform?: {
    origin?: UdomPoint;
    operations?: UdomTransformOperation[];
  };
}

export interface UdomAsset {
  version: "0.1";
  minVersion?: `${number}.${number}`;
  generator?: string;
  copyright?: string;
  extensions?: Record<string, unknown>;
  extras?: unknown;
}

export interface UdomViewport {
  width: number;
  height: number;
  pixelRatio?: number;
  fit?: "contain" | "cover" | "stretch" | "none";
  extensions?: Record<string, unknown>;
  extras?: unknown;
}

export interface UdomStyleDefinition {
  id: string;
  style: UdomStyle;
  extensions?: Record<string, unknown>;
  extras?: unknown;
}

export interface UdomResource {
  id: string;
  type: "image" | "sprite" | "font";
  uri: string;
  mimeType?: string;
  hash?: string;
  width?: number;
  height?: number;
  extensions?: Record<string, unknown>;
  extras?: unknown;
}

export interface UdomNodeBase {
  id: string;
  styleRefs?: string[];
  style?: UdomStyle;
  bind?: Record<string, string>;
  extensions?: Record<string, unknown>;
  extras?: unknown;
}

export interface UdomTextNode extends UdomNodeBase {
  type: "text";
  value: string;
}

export interface UdomElementNode extends UdomNodeBase {
  type: "element";
  name:
    | "view"
    | "image"
    | "button"
    | "toggle"
    | "slider"
    | "text-input"
    | "scroll"
    | "embed";
  properties?: Record<string, unknown>;
  children?: UdomNode[];
  on?: Record<string, string>;
}

export type UdomNode = UdomElementNode | UdomTextNode;

export interface UdomDocument {
  asset: UdomAsset;
  viewport: UdomViewport;
  root: UdomNode;
  styles?: UdomStyleDefinition[];
  resources?: UdomResource[];
  extensionsUsed?: string[];
  extensionsRequired?: string[];
  extensions?: Record<string, unknown>;
  extras?: unknown;
}

export interface RenderToUdomOptions {
  viewport: UdomViewport;
  asset?: Omit<UdomAsset, "version">;
  styles?: UdomStyleDefinition[];
  resources?: UdomResource[];
  extensionsUsed?: string[];
  extensionsRequired?: string[];
  extensions?: Record<string, unknown>;
  extras?: unknown;
  supportedExtensions?: string[];
}

export interface CommonPrimitiveProps {
  id?: string;
  styleRefs?: string[];
  style?: UdomStyle;
  bind?: Record<string, string>;
  extensions?: Record<string, unknown>;
  extras?: unknown;
}

export interface ViewProps extends CommonPrimitiveProps {
  children?: ReactNode;
}

export interface TextProps extends CommonPrimitiveProps {
  value?: string | number;
  children?: ReactNode;
}

export interface ImageProps extends CommonPrimitiveProps {
  src: string;
  fit?: "fill" | "contain" | "cover" | "none";
  position?: UdomPoint;
  children?: never;
}

export interface ButtonProps extends CommonPrimitiveProps {
  disabled?: boolean;
  on?: Partial<Record<"activate" | "focus" | "blur", string>>;
  children?: ReactNode;
}

export interface UdomDiagnostic {
  code: string;
  severity: "error";
  pointer: string;
  nodeId?: string;
  message: string;
}

