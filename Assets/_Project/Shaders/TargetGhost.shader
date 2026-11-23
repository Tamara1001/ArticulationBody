// Fantasma del target: translúcido con borde fresnel. _Alpha se ajusta por MaterialPropertyBlock.
Shader "Station/TargetGhost"
{
    Properties
    {
        _Color ("Color", Color) = (0.25, 0.85, 1.0, 1)
        _Alpha ("Alpha", Range(0, 1)) = 1
        _Fill ("Relleno", Range(0, 1)) = 0.12
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Ghost"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Alpha;
                half _Fill;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 N = normalize(i.normalWS);
                float3 V = normalize(GetWorldSpaceViewDir(i.positionWS));
                float f = pow(1.0 - saturate(abs(dot(N, V))), 2.0);
                // Líneas de barrido horizontales para que se lea como "holograma"
                float scan = 0.5 + 0.5 * sin(i.positionWS.y * 120.0 + _Time.y * 6.0);
                float a = saturate(_Fill + f * 0.75 + scan * 0.06) * _Alpha;
                return half4(_Color.rgb * (0.8 + f), a);
            }
            ENDHLSL
        }
    }
}
