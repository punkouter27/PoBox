// The crowd: one mesh of camera-facing quads, one draw call for the whole hall.
//
// Every quad carries its pivot in POSITION, its corner (-0.5..0.5, 0..1) in TEXCOORD1, a random phase and
// energy in TEXCOORD2, and its shirt colour in COLOR. The vertex shader turns each quad to face the camera
// about the vertical and moves it with the fight: an idle sway, a bounce whose height and rate follow
// _PoBoxCrowdMood (the excitement reading) and a jump on _PoBoxCrowdBurst (a clean heavy hit, a knockdown).
//
// The figure: by default an impostor from the crowd atlas (Textures/Crowd, baked in
// BLENDER/2026-10-31_crowd, see its README): 16 people (columns) x 8 rows of pose and view, 1.15 x 2.30 m a
// cell, pivot on the seat. The seed picks the person, the angle between where the figure faces (the ring
// centre) and the camera picks the view (front, nose-left, nose-right), and the mood picks the pose: seated,
// on its feet with the arms up when the hall gets going (each figure has its own threshold, so the stand
// rises in a ragged wave), or holding up a phone. It is lit with the atlas's normals (main light, ambient
// probe, and a fixed spill from the lamps over the ring), the garment takes the figure's shirt colour, and
// mask.A is the ambient occlusion.
//
// _CROWD_PROCEDURAL (the "Procedural figures" toggle) is the old cheap path: a head and shoulders drawn in
// the fragment shader, no textures sampled. Use a material with it on for a tier that cannot afford the atlas.
//
// Three more things it does with what ArenaMood sends it:
//   _PoBoxCrowdFlash   camera flashes. On a big moment a share of the figures each fire a flash, a different
//                      share every fourteenth of a second, bright enough to bloom (at the hands: mask.B).
//   _PoBoxCrowdPhones  phone lights. During the walk-on, a count and the result a share of the figures hold
//                      up a phone whose screen lights (mask.B on the phone rows).
//   _PoBoxCrowdSide    who just scored: -1 red, +1 blue. Each half of the hall sits behind its own corner,
//                      two figures in five there wear its colour, and they are the ones who jump highest
//                      when their fighter lands.
Shader "PoBox/Crowd"
{
    Properties
    {
        [Toggle(_CROWD_PROCEDURAL)] _Procedural ("Procedural figures (cheap fallback, no atlas)", Float) = 0
        [NoScaleOffset] _Albedo ("Atlas albedo (sRGB, A = coverage)", 2D) = "white" {}
        [NoScaleOffset] _Normal ("Atlas normal (impostor space, linear)", 2D) = "bump" {}
        [NoScaleOffset] _Mask ("Atlas mask (R garment, G skin, B light spot, A AO)", 2D) = "white" {}
        _Cutoff ("Alpha cut-off", Range(0, 1)) = 0.5
        _CellSize ("Impostor cell (m): width, height; pivot height in the cell (0..1)", Vector) = (1.15, 2.30, 0.2174, 0)
        _PivotDrop ("Pivot drop (m): mesh pivot above the seat", Float) = 0.1
        _RingCentre ("Ring centre (world x, z)", Vector) = (0, 0, 0, 0)
        _LitBrightness ("Impostor brightness", Range(0, 4)) = 1
        _AmbientGain ("Ambient (light probe) gain", Range(0, 4)) = 1
        _MainLightGain ("Main light gain", Range(0, 2)) = 0.5
        _SpillColor ("Ring spill colour (from the lamps over the ring)", Color) = (0.55, 0.5, 0.42, 1)
        _SpillHeight ("Ring spill height (m)", Float) = 6

        _Width ("Procedural figure width (m)", Float) = 0.62
        _Height ("Procedural figure height (m)", Float) = 0.95
        _Brightness ("Procedural brightness", Range(0, 2)) = 0.55
        _SkinColor ("Procedural skin", Color) = (0.72, 0.55, 0.44, 1)
        _RedCorner ("Red corner's colour", Color) = (0.89, 0.24, 0.24, 1)
        _BlueCorner ("Blue corner's colour", Color) = (0.20, 0.49, 0.94, 1)
        _FlashGain ("Flash brightness", Range(0, 20)) = 9
        _PhoneGain ("Phone light brightness", Range(0, 10)) = 1.6
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        LOD 100

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite On
            // With MSAA the cut-out edges are smoothed by coverage (URP's AlphaClip sharpens the alpha for it);
            // without MSAA AlphaClip falls back to a plain clip.
            AlphaToMask On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_local _ _CROWD_PROCEDURAL
            // AlphaClip / alpha-to-coverage helpers in ShaderVariablesFunctions.hlsl exist only with this defined.
            #define _ALPHATEST_ON 1
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // The same for every variant, so the SRP Batcher keeps one layout.
            CBUFFER_START(UnityPerMaterial)
                float _Cutoff;
                float4 _CellSize;
                float _PivotDrop;
                float4 _RingCentre;
                float _LitBrightness;
                float _AmbientGain;
                float _MainLightGain;
                half4 _SpillColor;
                float _SpillHeight;
                float _Width;
                float _Height;
                float _Brightness;
                half4 _SkinColor;
                half4 _RedCorner;
                half4 _BlueCorner;
                float _FlashGain;
                float _PhoneGain;
            CBUFFER_END

            TEXTURE2D(_Albedo); SAMPLER(sampler_Albedo);
            TEXTURE2D(_Normal);
            TEXTURE2D(_Mask);

            // Set every frame by ArenaMood.
            float _PoBoxCrowdMood;
            float _PoBoxCrowdBurst;
            float _PoBoxCrowdFlash;
            float _PoBoxCrowdPhones;
            float _PoBoxCrowdSide;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 corner : TEXCOORD1;
                float2 seed : TEXCOORD2;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                // x: fog, y: lift (0..1, on its feet / jumping), z: normal x sign (-1 on a mirrored cell), w: skin tone
                float4 misc : TEXCOORD1;
                // x: this figure's flash is firing, y: it is holding up a phone, z: where across the quad the phone is (procedural)
                float3 lights : TEXCOORD2;
                // xy: the quad's right (world x, z), the direction u grows; z unused
                float3 basis : TEXCOORD3;
                // direction from the figure to the lamps over the ring
                float3 spill : TEXCOORD4;
            };

            float hash2(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 pivot = TransformObjectToWorld(v.positionOS.xyz);
                float2 fromRing = pivot.xz - _RingCentre.xy;

                float3 toCam = _WorldSpaceCameraPos - pivot;
                toCam.y = 0;
                float3 fwd = normalize(toCam + float3(0, 0, 1e-4));
                // The camera's right: the direction the atlas's u grows. (cross(up, fwd) would be its left and
                // mirror every figure.)
                float3 right = normalize(cross(fwd, float3(0, 1, 0)));

                // Which corner this seat is behind: red is at (-x, -z), blue at (+x, +z).
                float side = dot(normalize(fromRing + float2(1e-4, 0)), float2(0.7071, 0.7071));
                float blueSide = smoothstep(-0.15, 0.15, side);
                float fan = step(hash2(v.seed * 31.7 + 4.2), 0.4);
                // +1 when this figure's own fighter has just scored, -1 when the other has.
                float cheer = _PoBoxCrowdSide * (blueSide * 2 - 1);

                float mood = saturate(_PoBoxCrowdMood);
                float burst = saturate(_PoBoxCrowdBurst) * (1.0 + 0.5 * cheer * fan);
                float phase = v.seed.x * 6.2831853;
                float energy = 0.4 + 0.6 * v.seed.y;
                float t = _Time.y;
                float bob = sin(t * 1.7 + phase) * 0.015 * (0.3 + mood);
                float bounce = max(0, sin(t * (4.0 + 2.0 * mood) + phase)) * 0.10 * mood * energy;
                float jump = max(0, sin(t * 9.0 + phase)) * 0.30 * burst * energy;
                float lean = sin(t * 1.3 + phase * 1.7) * 0.05 * (0.2 + mood);

                // A flash is one figure in about eighty at the peak, a different eightieth every 1/14 s (one in twelve read as
                // a strobe on the phone, owner 2026-11-03).
                float tick = floor(t * 14.0);
                float fire = step(hash2(v.seed * 97.0 + tick * 0.618), saturate(_PoBoxCrowdFlash) * 0.0125);
                float phone = step(hash2(v.seed * 53.0 + 9.1), saturate(_PoBoxCrowdPhones) * 0.05);
                o.lights = float3(fire, phone, 0.25 + 0.5 * hash2(v.seed * 11.0));

                float cy = v.corner.y;
                float3 up = float3(0, 1, 0);
                float nflip = 1;
#if defined(_CROWD_PROCEDURAL)
                float3 pos = pivot
                           + right * (v.corner.x * _Width + lean * cy)
                           + up * (cy * _Height + bob + bounce + jump);
                o.uv = v.uv;
                float stand = 0;
#else
                // The cell is 1.15 x 2.30 m with the seat at 0.2174 of its height; the mesh's pivots sit about
                // 0.1 m above the seat.
                float3 seat = pivot - up * _PivotDrop;
                float3 pos = seat
                           + right * (v.corner.x * _CellSize.x + lean * cy)
                           + up * ((cy - _CellSize.z) * _CellSize.y + bob + bounce + jump);

                // Person: one of the sixteen columns, from the seed.
                float person = min(floor(hash2(v.seed * 17.3 + 1.7) * 16.0), 15.0);

                // View: the figure faces the ring. Which way does its nose point on screen?
                float2 facing = normalize(-fromRing + 1e-4);
                float nose = dot(facing, right.xz);                     // > 0: nose to image right
                float view = abs(nose) < 0.34 ? 0 : (nose > 0 ? 2 : 1); // 0 front, 1 nose-left, 2 nose-right (40 deg renders; 0.34 = sin 20)

                // Pose: seated unless this figure is up. Each has its own threshold, so the hall rises in a wave.
                float stand = step(hash2(v.seed * 7.1) * 0.8 + 0.1, max(mood * energy * 0.6, burst));
                float row = phone > 0.5 ? (view == 0 ? 6 : 7) : (stand > 0.5 ? 3 : 0) + view;
                float mirror = (phone > 0.5 && view == 1) ? 1 : 0;      // row 7 is nose-right only
                stand = max(stand, phone);

                float lu = v.corner.x + 0.5;
                lu = mirror > 0.5 ? 1 - lu : lu;
                o.uv = float2(person / 16.0, 1.0 - (row + 1) / 8.0) + float2(lu, cy) * float2(1.0 / 16.0, 1.0 / 8.0);
                nflip = mirror > 0.5 ? -1 : 1;
#endif

                o.positionCS = TransformWorldToHClip(pos);
                // The fans wear their corner's colour.
                half3 cornerColour = lerp(_RedCorner.rgb, _BlueCorner.rgb, blueSide);
                o.color = half4(lerp(v.color.rgb, cornerColour * 0.8, fan * 0.8), 1);
                o.misc = float4(ComputeFogFactor(o.positionCS.z), saturate(bounce * 6 + jump * 3 + stand * 0.3), nflip,
                                0.88 + 0.24 * hash2(v.seed * 5.3 + 2.9));
                o.basis = float3(right.x, right.z, 0);
                o.spill = normalize(float3(_RingCentre.x, _SpillHeight, _RingCentre.y) - pivot);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half fog = i.misc.x;
                half lift = i.misc.y;
#if defined(_CROWD_PROCEDURAL)
                // Head: a circle near the top. Shoulders: an ellipse below it, cut off flat at the seat.
                float2 p = i.uv;
                float head = length((p - float2(0.5, 0.78)) / float2(0.17, 0.18));
                float body = length((p - float2(0.5, 0.18)) / float2(0.46, 0.52));
                float inside = min(head, body);

                // The flash is held in front of the face; the phone above one shoulder.
                float flash = saturate(1.0 - length((p - float2(0.5, 0.72)) / float2(0.20, 0.13))) * i.lights.x;
                float phone = saturate(1.0 - length((p - float2(i.lights.z, 0.93)) / float2(0.05, 0.034))) * i.lights.y;
                clip(max(1.0 - inside, max(flash, phone) - 0.02));

                half isHead = step(head, 1.0);
                half3 shirt = i.color.rgb;
                half3 col = lerp(shirt, _SkinColor.rgb, isHead);
                // Lit from the ring: brighter towards the top of the figure, and brighter when on its feet.
                half shade = _Brightness * (0.55 + 0.45 * p.y) * (1.0 + 0.5 * lift);
                col *= shade * step(inside, 1.0);
                col = MixFog(col, fog);
                // Lights are not fogged: they are what is seen through it.
                col += half3(1.0, 0.97, 0.92) * flash * flash * _FlashGain;
                col += half3(0.85, 0.92, 1.0) * phone * _PhoneGain;
                return half4(col, 1);
#else
                half4 a = SAMPLE_TEXTURE2D(_Albedo, sampler_Albedo, i.uv);
                half alpha = AlphaClip(a.a, _Cutoff);
                half4 m = SAMPLE_TEXTURE2D(_Mask, sampler_Albedo, i.uv);
                half3 n = SAMPLE_TEXTURE2D(_Normal, sampler_Albedo, i.uv).xyz * 2 - 1;
                n.x *= i.misc.z;

                // Impostor space: x along the quad's right, y up, z towards the camera.
                float3 R = float3(i.basis.x, 0, i.basis.y);
                float3 F = float3(R.z, 0, -R.x);
                float3 N = normalize(n.x * R + n.y * float3(0, 1, 0) + n.z * F);

                // The garment takes the figure's colour; skin varies a little from figure to figure.
                half3 col = a.rgb * lerp(1, i.color.rgb, m.r);
                col *= lerp(1, i.misc.w, m.g);

                half ao = m.a;
                Light L = GetMainLight();
                half3 light = SampleSH(N) * (_AmbientGain * ao)
                            + L.color * (L.distanceAttenuation * saturate(dot(N, L.direction)) * _MainLightGain * (0.5 + 0.5 * ao))
                            // The lamps over the ring, wrapped so the side away from them is not black.
                            + _SpillColor.rgb * (saturate(dot(N, normalize(i.spill)) * 0.7 + 0.3) * ao);
                half3 lit = col * light * (_LitBrightness * (1.0 + 0.5 * lift));
                lit = MixFog(lit, fog);
                // Lights are not fogged: they are what is seen through it. mask.B is the hands on rows 0-5 and the
                // phone on rows 6-7.
                lit += half3(1.0, 0.97, 0.92) * (i.lights.x * m.b * _FlashGain);
                lit += half3(0.85, 0.92, 1.0) * (i.lights.y * m.b * _PhoneGain);
                return half4(lit, alpha);
#endif
            }
            ENDHLSL
        }
    }
    Fallback Off
}
