// Original depth-tested URP world labels; Unity font atlas remains engine-supplied.
Shader "SpatialFabric/Room Text"
{
    Properties { _MainTex ("Font Atlas", 2D) = "white" {} _Color ("Color", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Off
        Pass
        {
            Name "RoomText"
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
            CBUFFER_END
            struct appdata { float4 vertex:POSITION;float2 uv:TEXCOORD0;half4 color:COLOR; };
            struct v2f { float4 vertex:SV_POSITION;float2 uv:TEXCOORD0;half4 color:COLOR; };
            v2f vert(appdata v) { v2f o;o.vertex=TransformObjectToHClip(v.vertex.xyz);o.uv=TRANSFORM_TEX(v.uv,_MainTex);o.color=v.color*_Color;return o; }
            half4 frag(v2f i):SV_Target { half4 c=i.color;c.a*=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv).a;return c; }
            ENDHLSL
        }
    }
}
