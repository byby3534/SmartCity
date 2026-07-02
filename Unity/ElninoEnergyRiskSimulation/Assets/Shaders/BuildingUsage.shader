Shader "SmartCity/BuildingUsage"
{
    Properties
    {
        [Header(Heatmap Colors)]
        _SafeColor   ("Safe Color (Low)",   Color) = (0.0, 0.5, 1.0, 1.0)
        _DangerColor ("Danger Color (High)", Color) = (1.0, 0.1, 0.1, 1.0)
        _ColorMin    ("Color Range Min",    Range(0, 1)) = 0.0
        _ColorMax    ("Color Range Max",    Range(0, 1)) = 1.0

        [Header(Blackout)]
        _BlackoutColor("Blackout Color",      Color) = (0.02, 0.02, 0.02, 1.0)

        [Header(Lighting)]
        _AmbientStrength("Ambient Strength", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // ── 렌더링 전용 경량 구조체 (C# BuildingRenderData와 동일) ──
            struct BuildingRenderData
            {
                float reductionValue;  // 4 bytes — 수요감축 필요도 (0~1)
                int   isBlackout;      // 4 bytes — 정전 여부 (0 or 1)
            };

            StructuredBuffer<BuildingRenderData> _BuildingRenderBuffer;

            CBUFFER_START(UnityPerMaterial)
                float4 _SafeColor;
                float4 _DangerColor;
                float4 _BlackoutColor;
                float  _AmbientStrength;
                float  _ColorMin;
                float  _ColorMax;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv2        : TEXCOORD1;   // uv2.x = buildingDataIndex
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                nointerpolation uint dataIndex : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);

                // UV2.x 에 기록된 buildingId를 버퍼 인덱스로 사용
                output.dataIndex = (uint)input.uv2.x;
                return output;
            }

            // 수요감축 필요도(0~1)를 Safe → Danger 보간
            // _ColorMin~_ColorMax 범위를 0~1로 리매핑해 색 분포를 펼침
            float3 EvaluateHeatmap(float t)
            {
                float range = max(_ColorMax - _ColorMin, 1e-5);
                t = saturate((t - _ColorMin) / range);
                return lerp(_SafeColor.rgb, _DangerColor.rgb, t);
            }

            half4 frag(Varyings input) : SV_Target
            {
                BuildingRenderData data = _BuildingRenderBuffer[input.dataIndex];

                // ── 1. 블랙아웃 처리 ──
                if (data.isBlackout == 1)
                {
                    return half4(_BlackoutColor.rgb, 1.0);
                }

                // ── 2. 수요감축 필요도 히트맵 색상 ──
                float3 baseColor = EvaluateHeatmap(data.reductionValue);

                // ── 3. 기본 디퓨즈 라이팅 ──
                Light mainLight = GetMainLight();
                float3 normal   = normalize(input.normalWS);
                float  ndotl    = saturate(dot(normal, mainLight.direction));
                float3 diffuse  = baseColor * (mainLight.color * ndotl + _AmbientStrength);

                return half4(diffuse, 1.0);
            }
            ENDHLSL
        }

        // ── DepthNormalsOnly 패스 (Decal Layers 필터링용) ──
        Pass
        {
            Name "DepthNormalsOnly"
            Tags { "LightMode" = "DepthNormalsOnly" }

            ZWrite On

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment

            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #if defined(LOD_FADE_CROSSFADE)
                #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderVariablesFunctions.hlsl"

            struct AttributesDN
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct VaryingsDN
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            VaryingsDN DepthNormalsVertex(AttributesDN input)
            {
                VaryingsDN output = (VaryingsDN)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            void DepthNormalsFragment(
                VaryingsDN input,
                out half4 outNormalWS : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
#endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                #if defined(LOD_FADE_CROSSFADE)
                    LODFadeCrossFade(input.positionCS);
                #endif

                #if defined(_GBUFFER_NORMALS_OCT)
                    float3 normalWS = normalize(input.normalWS);
                    float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                    float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
                    half3 packedNormalWS = half3(PackFloat2To888(remappedOctNormalWS));
                    outNormalWS = half4(packedNormalWS, 0.0);
                #else
                    outNormalWS = half4(normalize(input.normalWS), 0.0);
                #endif

                #ifdef _WRITE_RENDERING_LAYERS
                    outRenderingLayers = EncodeMeshRenderingLayer();
                #endif
            }
            ENDHLSL
        }
    }

    FallBack Off
}
