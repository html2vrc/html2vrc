import type { ComponentType } from "react";
import type {
  ButtonProps,
  ImageProps,
  TextProps,
  ViewProps
} from "./types.js";

export const primitiveMarker = Symbol.for("@html2vrc/react.primitive");

export type PrimitiveName = "view" | "text" | "image" | "button";

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
