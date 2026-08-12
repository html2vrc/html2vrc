Shader "HTML2VRC/UI Box Border"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _RectSize ("Rect Size", Vector) = (100, 100, 0, 0)
        _OuterRadii ("Outer Radii", Vector) = (0, 0, 0, 0)
        _InnerSize ("Inner Size", Vector) = (100, 100, 0, 0)
        _InnerCenter ("Inner Center", Vector) = (0, 0, 0, 0)
        _InnerRadiiX ("Inner Radii X", Vector) = (0, 0, 0, 0)
        _InnerRadiiY ("Inner Radii Y", Vector) = (0, 0, 0, 0)
        _BorderWidths ("Border Widths", Vector) = (0, 0, 0, 0)
        _HasInner ("Has Inner Contour", Float) = 1
        _BorderLeftColor ("Left Color", Color) = (0, 0, 0, 0)
        _BorderTopColor ("Top Color", Color) = (0, 0, 0, 0)
        _BorderRightColor ("Right Color", Color) = (0, 0, 0, 0)
        _BorderBottomColor ("Bottom Color", Color) = (0, 0, 0, 0)
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                float2 localPosition : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _RectSize;
            float4 _OuterRadii;
            float4 _InnerSize;
            float4 _InnerCenter;
            float4 _InnerRadiiX;
            float4 _InnerRadiiY;
            float4 _BorderWidths;
            float _HasInner;
            fixed4 _BorderLeftColor;
            fixed4 _BorderTopColor;
            fixed4 _BorderRightColor;
            fixed4 _BorderBottomColor;
            float4 _ClipRect;

            float SelectCorner(float4 values, float2 localPosition)
            {
                float right = step(0.0, localPosition.x);
                float top = step(0.0, localPosition.y);
                float bottomValue = lerp(values.w, values.z, right);
                float topValue = lerp(values.x, values.y, right);
                return lerp(bottomValue, topValue, top);
            }

            float CircularRoundedRectDistance(float2 localPosition, float2 size, float4 radii)
            {
                float radius = SelectCorner(radii, localPosition);
                float2 halfSize = max(size * 0.5, float2(0.0001, 0.0001));
                float2 distanceToCorner = abs(localPosition) - halfSize + radius;
                return length(max(distanceToCorner, 0.0))
                       + min(max(distanceToCorner.x, distanceToCorner.y), 0.0)
                       - radius;
            }

            float EllipticalRoundedRectDistance(
                float2 localPosition,
                float2 size,
                float4 radiiX,
                float4 radiiY)
            {
                float radiusX = SelectCorner(radiiX, localPosition);
                float radiusY = SelectCorner(radiiY, localPosition);
                float2 halfSize = max(size * 0.5, float2(0.0001, 0.0001));
                float2 radii = max(float2(radiusX, radiusY), float2(0.0001, 0.0001));
                float2 cornerPosition = abs(localPosition) - (halfSize - radii);
                float2 positiveCorner = max(cornerPosition, 0.0);
                float ellipseDistance = (length(positiveCorner / radii) - 1.0) * min(radii.x, radii.y);

                float2 squareDistance = abs(localPosition) - halfSize;
                float rectangleDistance = length(max(squareDistance, 0.0))
                                          + min(max(squareDistance.x, squareDistance.y), 0.0);
                float hasEllipse = step(0.0002, min(radiusX, radiusY));
                return lerp(rectangleDistance, ellipseDistance, hasEllipse);
            }

            float ShapeAlpha(float distance)
            {
                float antialiasWidth = max(fwidth(distance), 0.0001);
                return saturate(0.5 - distance / antialiasWidth);
            }

            fixed4 ResolveBorderColor(float2 localPosition)
            {
                float2 halfSize = _RectSize.xy * 0.5;
                float leftDistance = max(0.0, localPosition.x + halfSize.x);
                float topDistance = max(0.0, halfSize.y - localPosition.y);
                float rightDistance = max(0.0, halfSize.x - localPosition.x);
                float bottomDistance = max(0.0, localPosition.y + halfSize.y);
                float leftScore = _BorderWidths.x > 0.0001
                    ? leftDistance / _BorderWidths.x
                    : 1000000.0;
                float topScore = _BorderWidths.y > 0.0001
                    ? topDistance / _BorderWidths.y
                    : 1000000.0;
                float rightScore = _BorderWidths.z > 0.0001
                    ? rightDistance / _BorderWidths.z
                    : 1000000.0;
                float bottomScore = _BorderWidths.w > 0.0001
                    ? bottomDistance / _BorderWidths.w
                    : 1000000.0;

                float bestScore = leftScore;
                fixed4 result = _BorderLeftColor;
                if (topScore < bestScore)
                {
                    bestScore = topScore;
                    result = _BorderTopColor;
                }
                if (rightScore < bestScore)
                {
                    bestScore = rightScore;
                    result = _BorderRightColor;
                }
                if (bottomScore < bestScore)
                {
                    result = _BorderBottomColor;
                }
                return result;
            }

            v2f vert(appdata_t input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.worldPosition = input.vertex;
                output.localPosition = input.vertex.xy;
                output.vertex = UnityObjectToClipPos(output.worldPosition);
                output.texcoord = input.texcoord;
                output.color = input.color * _Color;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float outerDistance = CircularRoundedRectDistance(
                    input.localPosition,
                    _RectSize.xy,
                    _OuterRadii);
                float outerAlpha = ShapeAlpha(outerDistance);
                float innerDistance = EllipticalRoundedRectDistance(
                    input.localPosition - _InnerCenter.xy,
                    _InnerSize.xy,
                    _InnerRadiiX,
                    _InnerRadiiY);
                float innerAlpha = ShapeAlpha(innerDistance) * step(0.5, _HasInner);
                float borderAlpha = outerAlpha * (1.0 - innerAlpha);

                fixed4 textureColor = (tex2D(_MainTex, input.texcoord) + _TextureSampleAdd) * input.color;
                fixed4 color = ResolveBorderColor(input.localPosition) * textureColor;
                color.a *= borderAlpha;

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(input.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
