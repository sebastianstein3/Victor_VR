// The assistant's hologram (HologramAssistant.cs): everything glows by adding its colour to what is behind
// it, like light, with no depth writes. Two modes, set per material:
//  * _Billboard 0: lines and ordinary meshes (the globe, rings, filaments, base, beam). Vertex colour is
//    the colour and, in alpha, how bright it is.
//  * _Billboard 1: camera-facing soft dots. Each dot is four vertices at the same centre (the position),
//    the corner in UV0 (-1 to 1) and the dot's radius in metres in UV1.x.
// _Tint multiplies the colour, _Intensity the brightness, _Warm (0 to 1) turns the colour amber, keeping its
// brightness (cyan cannot be warmed by multiplying: the hologram goes amber while it thinks), and _Flicker
// adds the faint shimmer of a projected image, running with height. Three effects, after the usual
// hologram-shader ideas (rim light, travelling light, stretched glints):
//  * _Fresnel > 0: the surface glows at its edges and is nearly clear face-on (needs normals): the crystal
//    shell round the globe.
//  * _Pulse > 0 on a line mesh with UV0 = (position along the line 0-1, phase): a small packet of light runs
//    along every line, as signals do along neurons; _PulseTime is the clock (the script advances it, faster
//    when the assistant is busy).
//  * a dot's UV1 is its radius across and up, so a dot can be a streak (the lens flare across the core).
Shader "CardboardXR/Hologram"
{
    Properties
    {
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Float) = 1
        _Warm ("Warm", Range(0, 1)) = 0
        _Fresnel ("Fresnel power (0: off)", Float) = 0
        _Pulse ("Travelling pulses", Range(0, 1)) = 0
        _PulseTime ("Pulse clock", Float) = 0
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
                float _Warm;
                float _Fresnel;
                float _Pulse;
                float _PulseTime;
                float _Billboard;
                float _Flicker;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
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
                float3 normalWS : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 world = TransformObjectToWorld(input.positionOS);
                output.uv = input.uv; // lines: along, phase. Dots: the corner, below
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                if (_Billboard > 0.5)
                {
                    // The dot's corner, along the camera's own axes; the object's scale scales the dot
                    float scale = length(float3(unity_ObjectToWorld._m00, unity_ObjectToWorld._m10, unity_ObjectToWorld._m20));
                    float3 right = UNITY_MATRIX_V[0].xyz;
                    float3 up = UNITY_MATRIX_V[1].xyz;
                    world += (right * input.uv.x * input.size.x + up * input.uv.y * input.size.y) * scale;
                }
                output.positionWS = world;
                output.positionCS = TransformWorldToHClip(world);
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // A soft round falloff for dots, none for lines; it reaches zero at the dot's edge, so a bright
                // dot (the core) doesn't show the square of its quad
                float d2 = dot(input.uv, input.uv);
                float soft = (_Billboard > 0.5) ? exp(-d2 * 3.5) * saturate(1.0 - d2) : 1.0;
                float shimmer = 1.0 - _Flicker * (0.5 + 0.5 * sin(_Time.y * 37.0 + input.positionWS.y * 120.0 + input.positionWS.x * 70.0));
                // Rim light: clear face-on, bright where the surface turns away
                float rim = 1.0;
                if (_Fresnel > 0.01)
                {
                    float3 toCamera = normalize(_WorldSpaceCameraPos - input.positionWS);
                    rim = 0.12 + pow(saturate(1.0 - abs(dot(normalize(input.normalWS), toCamera))), _Fresnel);
                }
                // A packet of light running along each line, at its own phase
                float pulse = 0.0;
                if (_Pulse > 0.001 && _Billboard < 0.5)
                {
                    float head = frac(_PulseTime + input.uv.y) * 2.5;
                    float along = (input.uv.x - head) * 10.0;
                    pulse = exp(-along * along) * _Pulse;
                }
                half3 colour = input.color.rgb;
                float luminance = dot(colour, half3(0.3, 0.59, 0.11));
                colour = lerp(colour, luminance * half3(1.9, 1.1, 0.5), _Warm);
                half3 rgb = colour * _Tint.rgb * (input.color.a * soft * rim * _Intensity * shimmer) * (1.0 + pulse * 5.0);
                rgb += half3(0.5, 0.45, 0.35) * (pulse * input.color.a * _Intensity); // the packet's white-hot core
                return half4(rgb, 1.0);
            }
            ENDHLSL
        }
    }
}
