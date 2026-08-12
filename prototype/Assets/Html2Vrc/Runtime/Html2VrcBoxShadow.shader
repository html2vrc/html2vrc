Shader "HTML2VRC/UI Box Shadow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _ShapeSize ("Shape Size", Vector) = (100, 100, 0, 0)
        _CornerRadii ("Corner Radii", Vector) = (0, 0, 0, 0)
        _BoxSize ("Box Size", Vector) = (100, 100, 0, 0)
        _BoxCornerRadii ("Box Corner Radii", Vector) = (0, 0, 0, 0)
        _Offset ("Inset Offset", Vector) = (0, 0, 0, 0)
        _Blur ("Blur Radius", Float) = 0
        _Inset ("Inset", Float) = 0
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
            float4 _ShapeSize;
            float4 _CornerRadii;
            float4 _BoxSize;
            float4 _BoxCornerRadii;
            float4 _Offset;
            float _Blur;
            float _Inset;
            float4 _ClipRect;

            float RoundedRectDistance(
                float2 localPosition,
                float2 shapeSize,
                float4 cornerRadii)
            {
                float right = step(0.0, localPosition.x);
                float top = step(0.0, localPosition.y);
                float bottomRadius = lerp(cornerRadii.w, cornerRadii.z, right);
                float topRadius = lerp(cornerRadii.x, cornerRadii.y, right);
                float radius = lerp(bottomRadius, topRadius, top);
                float2 halfSize = max(shapeSize * 0.5, float2(0.0001, 0.0001));
                float2 distanceToCorner = abs(localPosition) - halfSize + radius;
                return length(max(distanceToCorner, 0.0))
                       + min(max(distanceToCorner.x, distanceToCorner.y), 0.0)
                       - radius;
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
                float shapeDistance = RoundedRectDistance(
                    input.localPosition - _Offset.xy,
                    _ShapeSize.xy,
                    _CornerRadii);
                float antialiasWidth = max(fwidth(shapeDistance), 0.0001);
                float softness = max(_Blur, antialiasWidth);
                float outerAlpha = saturate(0.5 - shapeDistance / softness);

                float boxDistance = RoundedRectDistance(
                    input.localPosition,
                    _BoxSize.xy,
                    _BoxCornerRadii);
                float boxAntialiasWidth = max(fwidth(boxDistance), 0.0001);
                float boxAlpha = saturate(0.5 - boxDistance / boxAntialiasWidth);
                float insetAlpha = saturate(0.5 + shapeDistance / softness) * boxAlpha;
                float shadowAlpha = lerp(outerAlpha, insetAlpha, step(0.5, _Inset));
                fixed4 color = (tex2D(_MainTex, input.texcoord) + _TextureSampleAdd) * input.color;
                color.a *= shadowAlpha;

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
