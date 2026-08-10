import type { ComponentType } from "react";
import type {
  ButtonProps,
  EmbedProps,
  ImageProps,
  ScrollProps,
  SliderProps,
  TextProps,
  TextInputProps,
  ToggleProps,
  ViewProps
} from "./types.js";

export const primitiveMarker = Symbol.for("@html2vrc/react.primitive");

export type PrimitiveName =
  | "view"
  | "text"
  | "image"
  | "button"
  | "toggle"
  | "slider"
  | "text-input"
  | "scroll"
  | "embed";

export type PrimitiveComponent<Props> = ComponentType<Props> & {
  readonly [primitiveMarker]: PrimitiveName;
};

function createPrimitive<Props>(
  name: PrimitiveName,
  displayName: string
): PrimitiveComponent<Props> {
  const primitive = function Html2VrcPrimitive(): never {
    throw new Error(
      `${displayName} is an @html2vrc/react primitive and can only be evaluated by renderToUDOM().`
    );
  };

  Object.defineProperties(primitive, {
    displayName: {
      value: displayName
    },
    [primitiveMarker]: {
      value: name
    }
  });

  return primitive as unknown as PrimitiveComponent<Props>;
}

export const View = createPrimitive<ViewProps>("view", "View");
export const Text = createPrimitive<TextProps>("text", "Text");
export const Image = createPrimitive<ImageProps>("image", "Image");
export const Button = createPrimitive<ButtonProps>("button", "Button");
export const Toggle = createPrimitive<ToggleProps>("toggle", "Toggle");
export const Slider = createPrimitive<SliderProps>("slider", "Slider");
export const TextInput = createPrimitive<TextInputProps>(
  "text-input",
  "TextInput"
);
export const Scroll = createPrimitive<ScrollProps>("scroll", "Scroll");
export const Embed = createPrimitive<EmbedProps>("embed", "Embed");

export function getPrimitiveName(value: unknown): PrimitiveName | undefined {
  if (
    (typeof value === "function" || typeof value === "object") &&
    value !== null &&
    primitiveMarker in value
  ) {
    return (value as PrimitiveComponent<unknown>)[primitiveMarker];
  }
  return undefined;
}
