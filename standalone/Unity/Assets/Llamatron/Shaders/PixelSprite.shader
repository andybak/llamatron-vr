Shader "Llamatron/PixelSprite"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
        _Palette ("Original palette", 2D) = "white" {}
        _Indexed ("Palette-index pixels", Float) = 0
        _Background ("Arena background", Color) = (0,0,0,1)
        _ArenaFloor ("Arena floor", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" }
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            sampler2D _Palette;
            float _Indexed, _ArenaFloor;
            fixed4 _Background;
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 position:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.position = UnityObjectToClipPos(v.vertex); o.uv = v.uv; o.color = v.color;
                return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float4 color = tex2D(_MainTex, i.uv);
                clip(color.a - 0.5);
                if (_Indexed > 0.5)
                    color.rgb = tex2D(_Palette, float2((floor(color.r * 255.0 + 0.5) + 0.5) / 16.0, 0.5)).rgb;
                if (_ArenaFloor > 0.5) color.rgb = max(color.rgb, _Background.rgb);
                return fixed4(color.rgb * i.color.rgb, 1);
            }
            ENDCG
        }
    }
}
