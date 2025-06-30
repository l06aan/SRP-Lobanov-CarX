Shader "Custom/SRP_PBR_Universal"
{
    Properties
    {
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _NormalMap ("Normal Map", 2D) = "bump" {}
        _RoughnessMap ("Roughness Map", 2D) = "white" {}
        _MetallicMap ("Metallic Map", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _Roughness ("Roughness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0.0
        _RoughnessMapWeight ("RoughnessMap Weight", Range(0,1)) = 1
        _MetallicMapWeight ("MetallicMap Weight", Range(0,1)) = 1
        _ShadowStrength ("Shadow Strength", Range(0,1)) = 0.8
        _ShadowBias ("Shadow Bias", Range(0.0001, 0.05)) = 0.01
        _ShadowMapSize ("Shadow Map Size", Float) = 1024
        [Header(IBL)] _IBLIntensity ("IBL Intensity", Range(0,2)) = 1
        [Header(Aniso)] _Aniso ("Anisotropy", Range(-1,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            sampler2D _MainTex;
            sampler2D _NormalMap;
            sampler2D _RoughnessMap;
            sampler2D _MetallicMap;
            sampler2D _ShadowMap;
            samplerCUBE _EnvCubemap;
            float4x4 unity_ObjectToWorld;
            float4x4 unity_MatrixVP;
            float3 _WorldSpaceCameraPos;
            float4x4 _LightMatrixVP;
            float _ShadowStrength;
            float _ShadowBias;
            float _IBLIntensity;
            float _Aniso;
            float4 _Color;
            float _Roughness;
            float _Metallic;
            float _RoughnessMapWeight;
            float _MetallicMapWeight;
            float3 _DirectionalLightDirection;
            float3 _DirectionalLightColor;
            float4 _AmbientColor;

            struct appdata
            {
                float3 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 tangentWS : TEXCOORD1;
                float3 bitangentWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
                float3 worldPos : TEXCOORD4;
                float4 posShadow : TEXCOORD5;
            };

            v2f vert(appdata v)
            {
                v2f o;
                float4 worldPos = mul(unity_ObjectToWorld, float4(v.vertex, 1.0));
                o.pos = mul(unity_MatrixVP, worldPos);
                float3 normalWS = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));
                float3 tangentWS = normalize(mul((float3x3)unity_ObjectToWorld, v.tangent.xyz));
                float3 bitangentWS = cross(normalWS, tangentWS) * v.tangent.w;
                o.normalWS = normalWS;
                o.tangentWS = tangentWS;
                o.bitangentWS = bitangentWS;
                o.uv = v.uv;
                o.worldPos = worldPos.xyz;
                o.posShadow = mul(_LightMatrixVP, worldPos);
                return o;
            }

            float3 GetNormal(v2f i)
            {
                float3 n = tex2D(_NormalMap, i.uv).xyz * 2.0 - 1.0;
                float3x3 TBN = float3x3(i.tangentWS, i.bitangentWS, i.normalWS);
                return normalize(mul(n, TBN));
            }

            float SampleShadow(sampler2D shadowMap, float4 shadowCoord, float bias)
            {
                float2 uv = shadowCoord.xy / shadowCoord.w;
                float depth = shadowCoord.z / shadowCoord.w;
                float shadowMapDepth = tex2D(shadowMap, uv).r;
                if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1) return 1;
                return (depth - bias > shadowMapDepth) ? 0.0 : 1.0;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 albedo = tex2D(_MainTex, i.uv).rgb * _Color.rgb;
                float alpha = _Color.a;

                float3 normal = GetNormal(i);
                float roughnessMap = tex2D(_RoughnessMap, i.uv).r;
                float metallicMap = tex2D(_MetallicMap, i.uv).r;
                float roughness = lerp(_Roughness, roughnessMap, _RoughnessMapWeight);
                float metallic = lerp(_Metallic, metallicMap, _MetallicMapWeight);

                float3 lightDir = normalize(_DirectionalLightDirection);
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);

                float NdotL = max(0, dot(normal, lightDir));
                float3 diffuse = albedo * _DirectionalLightColor * NdotL;

                float3 halfDir = normalize(lightDir + viewDir);
                float3 t = normalize(i.tangentWS);
                float aniso = _Aniso;
                float3 anisoHalfDir = normalize(halfDir + t * aniso);
                float anisoDot = max(0, dot(normal, anisoHalfDir));
                float specular = pow(anisoDot, 1.0 / (roughness * roughness + 0.001));
                specular *= (1.0 - roughness);

                float3 color = diffuse * (1 - metallic) + specular * metallic;
                float shadow = SampleShadow(_ShadowMap, i.posShadow, _ShadowBias);
                color = lerp(color * shadow, color, 1 - _ShadowStrength);

                color += _AmbientColor.rgb * albedo;
                float3 envDiffuse = texCUBE(_EnvCubemap, normal).rgb;
                color += envDiffuse * albedo * _IBLIntensity * (1 - metallic);

                color = pow(saturate(color), 1.0/2.2);
                return float4(color, alpha);
            }
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode"="SRPShadowCaster" }
            ZWrite On
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex sc_vert
            #pragma fragment sc_frag
            uniform float4x4 unity_ObjectToWorld;
            uniform float4x4 _LightMatrixVP;
            struct appdata { float3 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; };
            v2f sc_vert(appdata v)
            {
                v2f o;
                float4 worldPos = mul(unity_ObjectToWorld, float4(v.vertex, 1));
                o.pos = mul(_LightMatrixVP, worldPos);
                return o;
            }
            float4 sc_frag(v2f i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
