// TelegraphOutline.shader
// Purpose: The telegraph rim (MonsterColorCompositor, palettes with telegraphOutline): an unlit inverted hull. Each vertex is pushed
//          out along its world normal by _OutlineWidth metres and only the back faces are drawn, so a solid coloured rim shows around
//          the silhouette. Single-pass instanced stereo (Quest multiview) through the URP instancing / stereo macros.
//          _OutlineColor and _OutlineWidth are set per renderer by a MaterialPropertyBlock. Visual only.
Shader "StrikeFit/TelegraphOutline"
{
    Properties
    {
        _OutlineColor ("Colour", Color) = (1, 0.3, 0.1, 1)
        _OutlineWidth ("Width (m)", Float) = 0.012
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" }
        Pass
        {
            Name "TelegraphOutline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                float _OutlineWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = normalize(TransformObjectToWorldNormal(input.normalOS));
                positionWS += normalWS * _OutlineWidth;
                output.positionCS = TransformWorldToHClip(positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return half4(_OutlineColor.rgb, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
