// Tela de holograma para o telao de instrucoes.
//
// Unlit e transparente de proposito: a sala tem luz de foco cirurgico, e uma tela que recebe
// luz deixa de parecer projetada e passa a parecer um papel preso na parede. Aqui a tela e a
// fonte de luz dela mesma.
//
// Custo no Quest: um quad, sem textura, sem sombra, sem ZWrite. Toda a imagem sai de contas no
// fragmento, entao nao ha memoria de textura nem amostragem.
Shader "VRSurgery/Hologram"
{
    Properties
    {
        _BaseColor      ("Cor", Color) = (0.35, 0.85, 1.0, 1.0)
        _Opacity        ("Opacidade do corpo", Range(0, 1)) = 0.16
        _ScanDensity    ("Linhas de varredura", Range(0, 600)) = 220
        _ScanDepth      ("Forca das linhas", Range(0, 1)) = 0.35
        _ScanSpeed      ("Velocidade da varredura", Range(-20, 20)) = -1.6
        _SweepSpeed     ("Velocidade do facho", Range(0, 4)) = 0.35
        _SweepWidth     ("Largura do facho", Range(0.01, 1)) = 0.14
        _EdgeGlow       ("Brilho da borda", Range(0, 8)) = 2.2
        _EdgeWidth      ("Espessura da borda", Range(0.001, 0.5)) = 0.045
        _Flicker        ("Tremulacao", Range(0, 1)) = 0.05
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Hologram"
            Tags { "LightMode" = "UniversalForward" }

            // Aditivo: a tela soma luz na cena em vez de tapar o que esta atras, que e o que
            // faz o olho ler como projecao e nao como vidro pintado.
            Blend SrcAlpha One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // CBUFFER nomeado assim para o SRP Batcher aceitar o material.
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _Opacity;
                float  _ScanDensity;
                float  _ScanDepth;
                float  _ScanSpeed;
                float  _SweepSpeed;
                float  _SweepWidth;
                float  _EdgeGlow;
                float  _EdgeWidth;
                float  _Flicker;
            CBUFFER_END

            Varyings vert (Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag (Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float t = _Time.y;

                // Corpo da tela, fraco: o texto e que tem de se ler, nao o fundo.
                float body = _Opacity;

                // Varredura horizontal fina subindo devagar.
                float scan = sin((uv.y * _ScanDensity) + (t * _ScanSpeed * 10.0));
                body *= 1.0 - _ScanDepth * (0.5 + 0.5 * scan);

                // Um facho mais largo atravessando a tela de tempos em tempos.
                float sweepY = frac(t * _SweepSpeed);
                float sweep = saturate(1.0 - abs(uv.y - sweepY) / _SweepWidth);
                body += sweep * sweep * 0.18;

                // Moldura acesa: distancia ate a borda mais proxima.
                float2 d = min(uv, 1.0 - uv);
                float edge = 1.0 - saturate(min(d.x, d.y) / _EdgeWidth);
                body += pow(edge, 2.0) * _EdgeGlow * 0.25;

                // Tremulacao: ruido barato preso ao tempo, sem textura.
                float n = frac(sin(t * 91.7) * 43758.5453);
                body *= 1.0 - _Flicker * n;

                return half4(_BaseColor.rgb * body, saturate(body));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
