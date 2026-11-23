// Shader industrial estilizado para URP (compatible WebGL2).
// - Iluminación toon suave (2 bandas) mezclable con Lambert, sombras de la luz principal.
// - Ambiente de hemisferio (globales _StationSky / _StationGround) + rim fresnel.
// - Patrones procedurales opcionales: líneas de panel (mundo), rayas de peligro (objeto), cinta de embalaje.
// - Outline por casco invertido (pase SRPDefaultUnlit) usando normales suavizadas en UV3.
Shader "Station/IndustrialToon"
{
    Properties
    {
        _BaseColor ("Color base", Color) = (0.8, 0.8, 0.8, 1)
        _Toon ("Toon (0 = Lambert, 1 = bandas)", Range(0, 1)) = 0.7
        _RimStrength ("Rim", Range(0, 1)) = 0.25
        [HDR] _EmissionColor ("Emisión", Color) = (0, 0, 0, 1)

        _PanelStrength ("Líneas de panel", Range(0, 1)) = 0
        _PanelScale ("Tamaño de panel (m)", Float) = 1
        _PanelVariation ("Variación por panel", Range(0, 0.2)) = 0.04

        _StripeStrength ("Rayas de peligro", Range(0, 1)) = 0
        _StripeColor ("Color de raya", Color) = (0.08, 0.08, 0.09, 1)
        _StripeScale ("Frecuencia de rayas", Float) = 6

        _TapeStrength ("Cinta", Range(0, 1)) = 0
        _TapeColor ("Color de cinta", Color) = (0.75, 0.62, 0.42, 1)
        _TapeWidth ("Ancho de cinta (m)", Float) = 0.05
        _TapeTop ("Media altura de la caja (m)", Float) = 0.1

        _OutlineScale ("Escala de outline", Range(0, 3)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half _Toon;
            half _RimStrength;
            half4 _EmissionColor;
            half _PanelStrength;
            float _PanelScale;
            half _PanelVariation;
            half _StripeStrength;
            half4 _StripeColor;
            float _StripeScale;
            half _TapeStrength;
            half4 _TapeColor;
            float _TapeWidth;
            float _TapeTop;
            half _OutlineScale;
        CBUFFER_END

        half4 _StationSky;
        half4 _StationGround;
        half4 _StationOutlineColor;
        float _StationOutlineWidth;
        ENDHLSL

        // ---------------------------------------------------------------- Forward
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                float3 normalOS : TEXCOORD3;
                float fogFactor : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionOS = v.positionOS.xyz;
                o.normalOS = v.normalOS;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            half3 ApplyPatterns(half3 albedo, float3 posWS, float3 nWS, float3 posOS, float3 nOS)
            {
                // Líneas de panel triplanares (mundo): eje dominante de la normal
                if (_PanelStrength > 0.001)
                {
                    float3 an = abs(nWS);
                    float2 uv = an.y > an.x && an.y > an.z ? posWS.xz : (an.x > an.z ? posWS.zy : posWS.xy);
                    float2 cell = uv / _PanelScale;
                    float2 g = abs(frac(cell - 0.5) - 0.5) * _PanelScale;
                    float d = min(g.x, g.y);
                    float lineMask = 1.0 - smoothstep(0.004, 0.009, d);
                    float var = (Hash21(floor(cell)) - 0.5) * 2.0 * _PanelVariation;
                    albedo *= (1.0 + var);
                    albedo *= 1.0 - lineMask * _PanelStrength * 0.55;
                }
                // Rayas de peligro diagonales (objeto: se mueven con la pieza)
                if (_StripeStrength > 0.001)
                {
                    float s = frac((posOS.x + posOS.y + posOS.z) * _StripeScale);
                    float m = smoothstep(0.48, 0.52, s);
                    albedo = lerp(albedo, _StripeColor.rgb, m * _StripeStrength);
                }
                // Cinta de embalaje: franja central sobre la tapa y bajando por los lados ±Z
                if (_TapeStrength > 0.001)
                {
                    float band = step(abs(posOS.x), _TapeWidth);
                    float onTop = step(0.5, nOS.y);
                    float onSide = step(0.5, abs(nOS.z)) * step(_TapeTop - 0.09, posOS.y);
                    albedo = lerp(albedo, _TapeColor.rgb, band * saturate(onTop + onSide) * _TapeStrength);
                }
                return albedo;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 N = normalize(i.normalWS);
                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light light = GetMainLight(shadowCoord);

                float ndl = dot(N, light.direction);
                float lambert = saturate(ndl);
                float toon = smoothstep(0.0, 0.05, ndl) * 0.7 + smoothstep(0.55, 0.6, ndl) * 0.3;
                float diffuse = lerp(lambert, toon, _Toon) * light.shadowAttenuation;

                half3 albedo = ApplyPatterns(_BaseColor.rgb, i.positionWS, N, i.positionOS, normalize(i.normalOS));
                half3 ambient = lerp(_StationGround.rgb, _StationSky.rgb, N.y * 0.5 + 0.5);

                float3 V = normalize(GetWorldSpaceViewDir(i.positionWS));
                float rim = pow(1.0 - saturate(dot(N, V)), 4.0) * _RimStrength;

                half3 color = albedo * (ambient * 0.8 + light.color * diffuse * 0.95);
                color += rim * _StationSky.rgb * 0.6;
                color += _EmissionColor.rgb;
                color = MixFog(color, i.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        // ---------------------------------------------------------------- Outline (casco invertido)
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float3 smoothNormal : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float fogFactor : TEXCOORD0;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 n = dot(v.smoothNormal, v.smoothNormal) > 0.01 ? v.smoothNormal : v.normalOS;
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 nWS = normalize(TransformObjectToWorldNormal(n));
                float dist = distance(posWS, GetCameraPositionWS());
                float width = _StationOutlineWidth * _OutlineScale * (1.0 + dist * 0.12);
                posWS += nWS * width;
                o.positionCS = TransformWorldToHClip(posWS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                clip(_OutlineScale - 0.001);
                return half4(MixFog(_StationOutlineColor.rgb, i.fogFactor), 1);
            }
            ENDHLSL
        }

        // ---------------------------------------------------------------- Sombras
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 Vert(Attributes v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(v.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDir = normalize(_LightPosition - posWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, lightDir));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return positionCS;
            }

            half4 Frag() : SV_Target { return 0; }
            ENDHLSL
        }

        // ---------------------------------------------------------------- Profundidad
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 Vert(Attributes v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                return TransformObjectToHClip(v.positionOS.xyz);
            }

            half4 Frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
