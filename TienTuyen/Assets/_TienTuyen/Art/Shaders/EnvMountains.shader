// The far landscape (EnvironmentBackdrop): forested ranges from real elevation data and the limestone towers in front
// of them. Meshes are authored at their virtual size, kilometres out; the vertex stage pulls every point towards the
// camera along its own view ray with a logarithmic squeeze (EnvSqueeze.hlsl), so the silhouette and parallax are
// unchanged but everything fits inside the gameplay camera's far plane. Shading and haze use the unsqueezed position.
// Surface, light and air are in EnvKarst.hlsl. Vertex colour: R baked sun visibility, G sky visibility, B cavity (0.5
// flat, lower in ravines and grooves), A height up a tower's wall (towers, _Tower = 1) or, on the river corridor, the distance
// to the river in quarter metres (KarstWater under 14 m; 255 elsewhere); UV0.x: degrees of elevation
// above the level the ranges in front reach, where the valley cloud lies (EnvKarstCloud.hlsl).
// The near rings of the ranges continue the terrain mesh: there the colour is EnvGround's far meadow (EnvTerrain.hlsl,
// from the ground layers EnvironmentGround publishes) under the same lighting, fading into the forested ranges past ~1 km.
Shader "TienTuyen/Environment/Mountains"
{
    Properties
    {
        _Tower ("Tower meshes (1) or ranges (0)", Float) = 0
        _CanopyDark ("Canopy dark", Color) = (0.08, 0.15, 0.05, 1)
        _CanopyLight ("Canopy light", Color) = (0.21, 0.34, 0.1, 1)
        _Rock ("Limestone", Color) = (0.82, 0.8, 0.74, 1)
        _RockWarm ("Limestone, cream wash", Color) = (0.88, 0.8, 0.66, 1)
        _RockDark ("Limestone water streaks", Color) = (0.3, 0.3, 0.29, 1)
        _CliffSlope ("Bare rock from slope (1 - normal.y), feather", Vector) = (0.52, 0.1, 0, 0)
        _Canopy ("Canopy: crown relief (m), tone in the gaps", Vector) = (4, 0.4, 0, 0)
        _Flutes ("Grooves: depth (m), width (m), length (m)", Vector) = (7, 16, 240, 0)
        _Strata ("Beds: thickness (m), overhang depth (m), warp (m), bench forest", Vector) = (60, 4, 40, 0.9)
        _KarstRock ("Rock texture (rgb albedo, a height)", 2D) = "grey" {}
        _RockTex ("Rock texture: tile (m), relief (m), contrast", Vector) = (140, 10, 1.15, 0)
        _Light ("Light: sun gain, shadow coolness, sunward rim", Vector) = (1.15, 0.8, 1, 0)
        _Rim ("Backlit ridge rim", Range(0, 4)) = 2
        _Translucency ("Backlit forest", Range(0, 1)) = 0.5
        _Aerial ("Aerial perspective: distance (m), strength, near-layer darkening", Vector) = (3000, 0.85, 0.25, 0)
        _AerialTint ("Aerial perspective tint (x horizon luminance)", Color) = (0.7, 0.82, 1.06, 1)
        _MistColor ("Valley mist", Color) = (0.88, 0.88, 0.84, 1)
        _MistHeight ("Mist thickness (m)", Float) = 140
        _MistStrength ("Mist strength", Range(0, 1)) = 0.7
        _CloudSea ("Valley cloud: thickness over the nearer ridge (deg), billow, start (m), strength", Vector) = (3, 1.0, 2200, 1)
        _Squeeze ("Squeeze start (m), scale (m), 30 km at (m), terrain half size (m)", Vector) = (300, 15, 414, 340)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest+40" }   // last of the opaques: drawn only where the near ground, grass and leaves leave it showing

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "EnvCommon.hlsl"
        #include "EnvSqueeze.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half _Tower;
            half4 _CanopyDark;
            half4 _CanopyLight;
            half4 _Rock;
            half4 _RockWarm;
            half4 _RockDark;
            float4 _CliffSlope;
            float4 _Flutes;
            float4 _Canopy;
            float4 _Strata;
            float4 _RockTex;
            float4 _Light;
            half _Rim;
            half _Translucency;
            float4 _Aerial;
            half4 _AerialTint;
            half4 _MistColor;
            float _MistHeight;
            half _MistStrength;
            float4 _CloudSea;
            float4 _Squeeze;
        CBUFFER_END

        // Where a virtual point is drawn (EnvSqueeze.hlsl).
        float3 MountainSqueeze(float3 virtualWS) { return EnvSqueeze(virtualWS, GetCameraPositionWS(), _Squeeze); }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "EnvTerrain.hlsl"
            TEXTURE2D(_KarstRock); SAMPLER(sampler_KarstRock);
            #define KARST_ROCK(uv) SAMPLE_TEXTURE2D(_KarstRock, sampler_KarstRock, uv)
            // The sky's photo, blurred (EnvironmentBackdrop's mipmapped copy; level 3 ~ 11 deg a texel), and its globals.
            TEXTURE2D(_KarstSky); SAMPLER(sampler_KarstSky);
            float4 _KarstSkyParams;
            #define KARST_SKY(uv) SAMPLE_TEXTURE2D_LOD(_KarstSky, sampler_KarstSky, uv, 3).rgb
            #include "EnvKarst.hlsl"

            TEXTURE2D(_EnvFarGrass); SAMPLER(sampler_EnvFarGrass);
            TEXTURE2D(_EnvFarNeedles);
            TEXTURE2D(_EnvFarSoil);
            TEXTURE2D(_EnvFarRock);
            half4 _EnvFarGrassTint, _EnvFarNeedlesTint, _EnvFarSoilTint, _EnvFarRockTint;
            float4 _EnvFarRockSlope;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 virtualWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half4 color : TEXCOORD2;
                float rise : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.virtualWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(MountainSqueeze(output.virtualWS));
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color;
                output.rise = input.uv.x;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 p = input.virtualWS;
                float3 normal = normalize(input.normalWS);
                float3 view = p - GetCameraPositionWS();
                float distance = length(view);
                float3 viewDir = view / distance;
                half cavity = input.color.b * 2.0 - 1.0;
                // Near rings: the meadow hills of the terrain mesh, lit like EnvGround (no baked visibility, which was
                // computed for the real relief, not GroundHeight's). No branch round the karst surface: its relief
                // takes screen derivatives, which a branch breaks along every silhouette.
                half nearField = (1.0 - smoothstep(300.0, 800.0, distance)) * (1.0 - _Tower);
                half vegetation;
                half3 albedo = KarstSurface(p, normal, cavity, _Tower, input.color.a, distance, vegetation);
                half sunVisible = input.color.r;
                half skyVisible = input.color.g;
                UNITY_BRANCH if (nearField > 0.001)
                {
                    half3 grass = SAMPLE_TEXTURE2D_LOD(_EnvFarGrass, sampler_EnvFarGrass, 0.5, 12).rgb * _EnvFarGrassTint.rgb;
                    half3 needles = SAMPLE_TEXTURE2D_LOD(_EnvFarNeedles, sampler_EnvFarGrass, 0.5, 12).rgb * _EnvFarNeedlesTint.rgb;
                    half3 soil = SAMPLE_TEXTURE2D_LOD(_EnvFarSoil, sampler_EnvFarGrass, 0.5, 12).rgb * _EnvFarSoilTint.rgb;
                    half3 bare = SAMPLE_TEXTURE2D_LOD(_EnvFarRock, sampler_EnvFarGrass, 0.5, 12).rgb * _EnvFarRockTint.rgb;
                    half3 ground = EnvFarGround(p, normal, grass, needles, soil, bare, _EnvFarRockSlope.xy);
                    albedo = lerp(albedo, ground, nearField);
                    normal = normalize(lerp(normal, EnvHillNormal(p.xz, normal, 0.9), nearField));
                    sunVisible = lerp(sunVisible, 1.0, nearField);
                    skyVisible = lerp(skyVisible, 0.9, nearField);
                    vegetation *= 1.0 - nearField;
                }

                Light sun = GetMainLight();
                half3 color = KarstLight(albedo, normal, viewDir, sun.color, SampleSH(normal), sunVisible, skyVisible, cavity, vegetation, nearField);
                // The river corridor carries the distance to the river in alpha (x 4 m); everything else is 255.
                half fromRiver = input.color.a * 63.75;
                half water = (1.0 - smoothstep(13.0, 14.5, fromRiver)) * (1.0 - _Tower);
                UNITY_BRANCH if (water > 0.001)
                    color = KarstWater(color, p, viewDir, distance, water, 14.0 - fromRiver, sun.color, SampleSH(half3(0, 1, 0)), _Time.y);
                // The gorge mouth's forest caps keep half their own colour through the air: green jungle on the nearest
                // crowns, while the rock and every farther layer recede.
                half clear = _Tower * (1.0 - smoothstep(1600.0, 2800.0, distance)) * vegetation * 0.5;
                return half4(lerp(KarstAir(color, p, viewDir, distance, nearField, input.rise, sun.color, _Time.y), color * 1.3, clear), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            float4 DepthVert(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformWorldToHClip(MountainSqueeze(TransformObjectToWorld(positionOS.xyz)));
            }

            half DepthFrag(float4 positionCS : SV_POSITION) : SV_Target { return positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex NormalsVert
            #pragma fragment NormalsFrag

            struct NormalsVaryings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };

            NormalsVaryings NormalsVert(float4 positionOS : POSITION, float3 normalOS : NORMAL)
            {
                NormalsVaryings output;
                output.positionCS = TransformWorldToHClip(MountainSqueeze(TransformObjectToWorld(positionOS.xyz)));
                output.normalWS = TransformObjectToWorldNormal(normalOS);
                return output;
            }

            half4 NormalsFrag(NormalsVaryings input) : SV_Target { return half4(NormalizeNormalPerPixel(input.normalWS), 0); }
            ENDHLSL
        }
    }
}
