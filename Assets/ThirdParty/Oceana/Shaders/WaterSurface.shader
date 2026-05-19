Shader "Oceana/WaterSurface" {
    Properties {
        _Color ("Color", color) = (0.2, 0.6, 0.8, 1)
        _ColorFade ("Color Fade", color) = (0.2, 0.6, 0.8, 1)
        _NightColor ("Night Color", color) = (0.005, 0.025, 0.055, 1)
        _NightColorFade ("Night Color Fade", color) = (0.015, 0.04, 0.07, 1)
        _NightFoamBrightness ("Night Foam Brightness", range(0, 1)) = 0.22
        _NightSpecularBrightness ("Night Specular Brightness", range(0, 1)) = 0.08
        _SkyReflectionStrength ("Sky Reflection Strength", range(0, 1)) = 0.85
        _SkyReflectionFresnelPower ("Sky Reflection Fresnel Power", float) = 0.55
        _PlanarReflectionBrightness ("Reflection Brightness", float) = 1.35
        _SkyReflectionFallbackStrength ("Sky Reflection Fallback Strength", range(0, 1)) = 0.75
        _SkyReflectionMip ("Sky Reflection Blur", range(0, 7)) = 0.4
        _NightReflectionStrength ("Night Reflection Strength", range(0, 1)) = 0.35
        _CloudShadowReflectionStrength ("Cloud Shadow Reflection Strength", range(0, 1)) = 0.55

        _GlareSpecular ("Glare Specular Edge", range(0, 1)) = 0.6
        _GlareEdgeLow ("Glare Low Edge", range(0, 1)) = 0.8
        _GlareEdgeHigh ("Glare High Edge", range(0, 1)) = 0.9
        _GlareIntensity ("Glare Intensity", range(0, 1)) = 0.8

        _FresnelFade ("Fresnel Fade", float) = 100
        _FadeIntensity ("Fade Intensity", range(0, 1)) = 0.9
        _FresnelWater ("Fresnel Water", float) = 10

        _WindowEdgeLow ("Window Low Edge", range(0, 1)) = 0.5
        _WindowEdgeHigh ("Window High Edge", range(0, 1)) = 0.6

        _DisplaceEdgeClose ("Displace Edge Close", float) = 10
        _DisplaceEdgeFar ("Displace Edge Far", float) = 80

        _VisibleDepth ("Visible Depth", float) = 10
        _Refraction ("Refraction", float) = 0.1

        _FoamEdgeLow ("Foam Edge Low", float) = 0.4
        _FoamEdgeHigh ("Foam Edge High", float) = 0.4
        _FoamMask ("Foam Mask", 2D) = "white"{}
    }
    SubShader {
        Tags {"RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque"}

        HLSLINCLUDE

        cbuffer UnityPerMaterial {
            uniform float4 _Color;
            uniform float4 _ColorFade;
            uniform float4 _NightColor;
            uniform float4 _NightColorFade;
            uniform float _NightFoamBrightness;
            uniform float _NightSpecularBrightness;
            uniform float _SkyReflectionStrength;
            uniform float _SkyReflectionFresnelPower;
            uniform float _PlanarReflectionBrightness;
            uniform float _SkyReflectionFallbackStrength;
            uniform float _SkyReflectionMip;
            uniform float _NightReflectionStrength;
            uniform float _CloudShadowReflectionStrength;

            uniform float _GlareSpecular;
            uniform float _GlareEdgeLow;
            uniform float _GlareEdgeHigh;
            uniform float _GlareIntensity;

            uniform float _FresnelFade;
            uniform float _FadeIntensity;
            uniform float _FresnelWater;

            uniform float _WindowEdgeLow;
            uniform float _WindowEdgeHigh;

            uniform float _DisplaceEdgeClose;
            uniform float _DisplaceEdgeFar;

            uniform float _VisibleDepth;
            uniform float _Refraction;

            uniform float _FoamEdgeLow;
            uniform float _FoamEdgeHigh;
            uniform float4 _FoamMask_ST;
        }

        cbuffer FromDynamic {
            uniform float4 _ScrollMap_ST;
            uniform uint _VertexMipLevel;

            uniform float4 _Time;

            uniform float _SeaLevel;
            uniform float _DisplaceHeight;

            uniform float4x4 unity_MatrixVP;

            uniform float4 _ProjectionParams;
            uniform float4 _ScreenParams;
            uniform float4 _ZBufferParams;
            uniform float3 _WorldSpaceCameraPos;
            uniform float4 _MainLightPosition;
            uniform float _DevsteadNightWaterFactor;
            uniform float4x4 _MainLightWorldToLight;
            uniform float _MainLightCookieTextureFormat;

            uniform SamplerState _bilinearRepeatSampler;
            uniform SamplerState _pointRepeatSampler;
            uniform SamplerState _pointClampSampler;
        }

        tbuffer MapBuffer {
            uniform Texture2D _ScrollMap;
            uniform Texture2D _SourceColor;
            uniform Texture2D _SourceDepth;

            uniform Texture2D _FoamMask;
            uniform Texture2D _MainLightCookieTexture;
            uniform TextureCube _GlossyEnvironmentCubeMap;
        }

        #include "include/ScreenSpaceFunctions.hlsl"
        #include "include/MapPacking.hlsl"

        float SpecularBRF(float3 viewDir, float3 normal, float3 lightDir) {
            float3 halfVector = normalize(viewDir + lightDir);
            return saturate(dot(halfVector, normal)) * saturate(sign(lightDir.y));
        }

        float3 SampleSkyReflection(float3 viewDir, float3 normal) {
            float3 reflectionDir = reflect(-viewDir, normal);
            return _GlossyEnvironmentCubeMap.SampleLevel(_bilinearRepeatSampler, reflectionDir, _SkyReflectionMip).rgb * _PlanarReflectionBrightness;
        }

        float SampleMainLightCookieValue(float3 positionWS) {
        #if defined(_LIGHT_COOKIES)
            if (_MainLightCookieTextureFormat < -0.5) {
                return 1.0;
            }

            float2 cookieUV = mul(_MainLightWorldToLight, float4(positionWS, 1.0)).xy * 0.5 + 0.5;
            float4 cookie = _MainLightCookieTexture.SampleLevel(_pointClampSampler, cookieUV, 0);

            if (_MainLightCookieTextureFormat > 1.5) {
                return cookie.r;
            }

            if (_MainLightCookieTextureFormat > 0.5) {
                return cookie.a;
            }

            return dot(cookie.rgb, float3(0.2126, 0.7152, 0.0722));
        #else
            return 1.0;
        #endif
        }

        ENDHLSL

        Pass {
            Cull [Off]

            HLSLPROGRAM
            #pragma vertex VertFunc
            #pragma fragment FragFunc
            #pragma multi_compile _ _LIGHT_COOKIES

            struct Varyings{
                float4 positionWS : TEXCOORD0;
                float4 positionSS : TEXCOORD1;
                float4 positionCS : SV_POSITION;
            };

            Varyings VertFunc(float3 positionOS : POSITION) {
                Varyings output = (Varyings)0;

                float3 directionWS = positionOS.xyz * _ProjectionParams.z * 1.37;
                output.positionWS = float4(directionWS + float3(_WorldSpaceCameraPos.x, 0, _WorldSpaceCameraPos.z), 1);
                float heightValue = _ScrollMap.SampleLevel(_bilinearRepeatSampler, output.positionWS.xz * _ScrollMap_ST.xy + _ScrollMap_ST.zw, 0).a;
                float displaceGrad = smoothstep(_DisplaceEdgeFar, _DisplaceEdgeClose, length(directionWS));
                output.positionWS.y = _SeaLevel + displaceGrad * heightValue * _DisplaceHeight;

                output.positionCS = mul(unity_MatrixVP, output.positionWS);
                output.positionSS = TransformClipToScreen(output.positionCS, _ProjectionParams);

                return output;
            }

            float4 FragFunc(Varyings input, bool isFrontFace : SV_ISFRONTFACE) : SV_TARGET {
                float4 sample = _ScrollMap.Sample(_bilinearRepeatSampler, input.positionWS.xz * _ScrollMap_ST.xy + _ScrollMap_ST.xz);
                float2 uvSS = TransformScreenToUV(input.positionSS);

                float3 viewVector = _WorldSpaceCameraPos.xyz - input.positionWS.xyz;
                float3 normal = UnpackRGBNormal(sample.rgb);
                float3 normalOptions[2] = {-normal, normal};
                normal = normalOptions[isFrontFace];

                float2 uvRefracted = uvSS + normal.xz * _Refraction;
                float sceneDepth = _SourceDepth.SampleLevel(_pointClampSampler, uvSS, 0).r;
                float refractedDepth = _SourceDepth.SampleLevel(_pointClampSampler, uvRefracted, 0).r;

                float invFoamMask = smoothstep(_FoamEdgeLow, _FoamEdgeHigh, LinearEyeDepth(sceneDepth, _ZBufferParams) - input.positionSS.w);
                float3 foamColor = _FoamMask.Sample(_bilinearRepeatSampler, input.positionWS.xz * _FoamMask_ST.xy + _FoamMask_ST.zw * _Time.y);

                float depthOptions[2] = {sceneDepth, refractedDepth};
                float2 uvOptions[2] = {uvSS, uvRefracted};

                bool isRefraction = LinearEyeDepth(refractedDepth, _ZBufferParams) > input.positionCS.w;
                sceneDepth = depthOptions[isRefraction];
                uvSS = uvOptions[isRefraction];

                float waterDepth = _ProjectionParams.y + LinearEyeDepth(sceneDepth, _ZBufferParams) - input.positionSS.w;
                float3 sceneColor = _SourceColor.SampleLevel(_pointClampSampler, uvSS, 0).rgb;

                float3 viewDir = normalize(viewVector);
                float mainLightCookie = SampleMainLightCookieValue(input.positionWS.xyz);
                float cloudShadow = lerp(1.0, mainLightCookie, _CloudShadowReflectionStrength);

                float frensel = 1 - saturate(dot(viewDir, normal));
                float fadeMask = pow(frensel, _FresnelFade) * _FadeIntensity;
                float waterMask = pow(frensel, _FresnelWater);
                float windowMask = smoothstep(_WindowEdgeLow, _WindowEdgeHigh, 1 - waterMask);
                float nightWaterFactor = saturate(_DevsteadNightWaterFactor);
                float3 waterColor = lerp(_Color.rgb, _NightColor.rgb, nightWaterFactor);
                float3 waterFadeColor = lerp(_ColorFade.rgb, _NightColorFade.rgb, nightWaterFactor);
                float foamBrightness = lerp(1.0, _NightFoamBrightness, nightWaterFactor);
                float specularBrightness = lerp(1.0, _NightSpecularBrightness, nightWaterFactor);
                float reflectionNightStrength = lerp(1.0, _NightReflectionStrength, nightWaterFactor);

                float3 skyReflection = SampleSkyReflection(viewDir, normal);
                float3 reflectionColor = skyReflection * _SkyReflectionFallbackStrength;
                reflectionColor *= reflectionNightStrength * cloudShadow;
                waterColor = lerp(waterColor, waterColor * max(reflectionColor, 0.05) * 1.7, _SkyReflectionFallbackStrength * reflectionNightStrength);
                waterFadeColor = lerp(waterFadeColor, reflectionColor, _SkyReflectionFallbackStrength * 0.75 * reflectionNightStrength);

                float3 surfaceColor = lerp(waterColor * waterColor, waterColor, waterMask);
                float specularMask = pow(SpecularBRF(viewDir, normal, _MainLightPosition.xyz), pow(2, _GlareSpecular * 8)) * _GlareIntensity * (1 - fadeMask);
                specularMask = smoothstep(_GlareEdgeLow, _GlareEdgeHigh, specularMask);
                specularMask *= specularBrightness * cloudShadow;

                float depthMask = 1 - saturate(waterDepth);
                surfaceColor = lerp(surfaceColor, sceneColor, windowMask * (1 - isFrontFace));
                surfaceColor = lerp(surfaceColor, sceneColor, depthMask * isFrontFace);
                float reflectionMask = saturate(pow(frensel, _SkyReflectionFresnelPower) * _SkyReflectionStrength * reflectionNightStrength) * isFrontFace;
                surfaceColor = lerp(surfaceColor, reflectionColor, reflectionMask);
                surfaceColor = lerp(surfaceColor, float3(1, 1, 1), (1 - invFoamMask) * foamColor * foamBrightness);
                surfaceColor = saturate(lerp(surfaceColor, waterFadeColor, fadeMask) + specularMask * isFrontFace);

                return float4(surfaceColor, 1);
            }
            ENDHLSL
        }
    }
}
