// Unlit vertex-colored shader for the generated wound strip.
// The Standard shader ignores mesh vertex colors, so the depth gradient baked into the
// wound mesh needs a shader that actually reads them. Deliberately minimal: the wound is a
// thin decal-like strip that reads best as flat colour, and this keeps it cheap in VR.
Shader "VRSurgery/WoundVertexColor"
{
    Properties
    {
        _Tint ("Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+1" }
        LOD 100

        Pass
        {
            Cull Off
            ZWrite On
            Offset -1, -1

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
            };

            fixed4 _Tint;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return i.color * _Tint;
            }
            ENDCG
        }
    }

    Fallback "Unlit/Color"
}
