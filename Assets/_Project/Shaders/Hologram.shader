// The assistant's hologram (HologramAssistant.cs): everything glows by adding its colour to what is behind
// it, like light, with no depth writes. Two modes, set per material:
//  * _Billboard 0: lines and ordinary meshes (the globe, rings, filaments, base, beam). Vertex colour is
//    the colour and, in alpha, how bright it is.
//  * _Billboard 1: camera-facing soft dots. Each dot is four vertices at the same centre (the position),
//    the corner in UV0 (-1 to 1) and the dot's radius in metres in UV1.x.
// _Tint multiplies the colour (the hologram warms up while it thinks), _Intensity the brightness, and
// _Flicker adds the faint shimmer of a projected image, running with height.
Shader "CardboardXR/Hologram"
{
    Properties
    {
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Float) = 1
        _Billboard ("Billboard (0 lines and meshes, 1 dots)", Float) = 0
        _Flicker ("Flicker", Range(0, 0.3)) = 0.06
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "Hologram"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                float _Intensity;
                float _Billboard;
                float _Flicker;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 size : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 world = TransformObjectToWorld(input.positionOS);
                output.uv = float2(0.0, 0.0);
                if (_Billboard > 0.5)
                {
                    // The dot's corner, along the camera's own axes; the object's scale scales the dot
                    float scale = length(float3(unity_ObjectToWorld._m00, unity_ObjectToWorld._m10, unity_ObjectToWorld._m20));
                    float3 right = UNITY_MATRIX_V[0].xyz;
                    float3 up = UNITY_MATRIX_V[1].xyz;
                    world += (right * input.uv.x + up * input.uv.y) * (input.size.x * scale);
                    output.uv = input.uv;
                }
                output.positionWS = world;
                output.positionCS = TransformWorldToHClip(world);
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // A soft round falloff for dots, none for lines
                float soft = (_Billboard > 0.5) ? exp(-dot(input.uv, input.uv) * 3.5) : 1.0;
                float shimmer = 1.0 - _Flicker * (0.5 + 0.5 * sin(_Time.y * 37.0 + input.positionWS.y * 120.0 + input.positionWS.x * 70.0));
                half3 rgb = input.color.rgb * _Tint.rgb * (input.color.a * soft * _Intensity * shimmer);
                return half4(rgb, 1.0);
            }
            ENDHLSL
        }
    }
}
