// Light-blue outline for "this is what your hand will take".
//
// An inverted hull: the mesh drawn again with only its back faces, pushed out along the normals
// by a fixed width in world space, so a rim of colour shows round the object's silhouette and
// the object itself hides the rest. One extra draw per outlined object and nothing else, which
// is why it suits a Quest better than a screen-space outline.
//
// Hard-edged meshes split their normals at the corners and the hull would crack there, so the
// scene builder bakes averaged normals into the fourth UV channel; meshes without them fall back
// to their own normals (organic models are smooth anyway).
Shader "VRSurgery/GrabOutline"
{
    Properties
    {
        _Color ("Cor", Color) = (0.45, 0.82, 1, 1)
        _Width ("Espessura (m)", Float) = 0.0025
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" }

        Pass
        {
            Name "Outline"
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
                half4 _Color;
                float _Width;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 smoothNormalOS : TEXCOORD3;
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

                float3 normalOS = dot(input.smoothNormalOS.xyz, input.smoothNormalOS.xyz) > 0.01
                    ? input.smoothNormalOS.xyz
                    : input.normalOS;

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = normalize(TransformObjectToWorldNormal(normalOS));
                positionWS += normalWS * _Width;
                output.positionCS = TransformWorldToHClip(positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return _Color;
            }
            ENDHLSL
        }
    }
}
