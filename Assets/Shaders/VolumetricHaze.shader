// The arena's haze (Fx/VolumetricHaze.cs): pass 0 marches the view ray through a height fog lit by the main light and
// Forward+'s lights with their shadows; pass 1 lays it over the frame (colour x transmittance + scattered light).
Shader "PoBox/VolumetricHaze"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        ENDHLSL

        Pass
        {
            Name "March"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS _ADDITIONAL_LIGHTS_VERTEX
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            // Hard shadow taps only: one per light a step (soft ones cost 9-16; the haze blurs the edge anyway).
            #pragma multi_compile _ _LIGHT_COOKIES
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            float4 _HazeParams;   // density, height falloff, anisotropy g, intensity
            float4 _HazeParams2;  // max distance, steps, frame index

            float Phase(float cosTheta, float g)
            {
                float g2 = g * g;
                return (1.0 - g2) / (4.0 * PI * pow(max(1e-4, 1.0 + g2 - 2.0 * g * cosTheta), 1.5));
            }

            float Noise(float2 pixel, float frame)
            {
                pixel += frame * 5.588238;
                return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float depth = SampleSceneDepth(uv);
                #if !UNITY_REVERSED_Z
                depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, depth);
                #endif
                float3 end = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                float3 start = _WorldSpaceCameraPos;
                float3 ray = end - start;
                float len = min(length(ray), _HazeParams2.x);
                float3 dir = normalize(ray);
                int steps = max(1, (int)_HazeParams2.y);
                float stepLen = len / steps;
                float jitter = Noise(input.positionCS.xy, _HazeParams2.z);

                float density = _HazeParams.x, falloff = _HazeParams.y, g = _HazeParams.z;
                float3 scattered = 0;
                float transmittance = 1;
                InputData inputData = (InputData)0;
                inputData.normalizedScreenSpaceUV = uv;

                [loop] for (int i = 0; i < steps; i++)
                {
                    float3 p = start + dir * (stepLen * (i + jitter));
                    float d = density * exp(-max(0, p.y) * falloff);
                    float3 light = 0;

                    float4 shadowCoord = TransformWorldToShadowCoord(p);
                    Light main = GetMainLight(shadowCoord, p, half4(1, 1, 1, 1));
                    light += main.color * main.shadowAttenuation * Phase(dot(dir, main.direction), g);

                    #if defined(_ADDITIONAL_LIGHTS)
                    inputData.positionWS = p;
                    uint count = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(count)
                        Light l = GetAdditionalLight(lightIndex, p, half4(1, 1, 1, 1));
                        light += l.color * l.distanceAttenuation * l.shadowAttenuation * Phase(dot(dir, l.direction), g);
                    LIGHT_LOOP_END
                    #endif

                    float ext = d * stepLen;
                    scattered += transmittance * light * d * stepLen;
                    transmittance *= exp(-ext);
                }
                return half4(scattered * _HazeParams.w, transmittance);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Composite"
            // out = scattered + destination x transmittance
            Blend One SrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
            }
            ENDHLSL
        }
    }
}
