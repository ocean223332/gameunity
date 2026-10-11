// Distant mountains built from real elevation data (EnvironmentBackdrop). Meshes are authored at their
// virtual size, kilometres out; the vertex stage pulls every point towards the camera along its own view
// ray with a logarithmic squeeze past _Squeeze.x metres, so the silhouette and parallax are unchanged but
// everything fits inside the gameplay camera's far plane. Shading and haze use the unsqueezed position.
// Vertex colour: R baked sun visibility, G sky visibility, B cavity (0.5 flat, lower in ravines), A unused.
Shader "TienTuyen/Environment/Mountains"
{
    Properties
    {
        _CanopyDark ("Canopy dark", Color) = (0.09, 0.14, 0.06, 1)
        _CanopyLight ("Canopy light", Color) = (0.2, 0.28, 0.11, 1)
        _Rock ("Limestone", Color) = (0.7, 0.67, 0.58, 1)
        _RockDark ("Limestone water stains", Color) = (0.27, 0.27, 0.25, 1)
        _CliffSlope ("Bare rock from slope (1 - normal.y)", Range(0, 1)) = 0.6
        _CliffHeight ("Bare rock above height (m)", Float) = 90
        _Ledges ("Forest ledges on cliffs: band height (m), cover", Vector) = (40, 0.6, 0, 0)
        _Rim ("Backlit ridge rim", Range(0, 4)) = 0.9
        _Aerial ("Aerial perspective: distance (m), strength", Vector) = (3200, 0.8, 0, 0)
        _AerialTint ("Aerial perspective tint (x horizon luminance)", Color) = (0.78, 0.88, 1.0, 1)
        _MistColor ("Valley mist", Color) = (0.86, 0.85, 0.8, 1)
        _MistHeight ("Mist thickness (m)", Float) = 110
        _MistStrength ("Mist strength", Range(0, 1)) = 0.6
        _Squeeze ("Squeeze start (m), scale (m)", Vector) = (300, 15, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+20" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "EnvCommon.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _CanopyDark;
            half4 _CanopyLight;
            half4 _Rock;
            half4 _RockDark;
            half _CliffSlope;
            float _CliffHeight;
            float4 _Ledges;
            half _Rim;
            float4 _Aerial;
            half4 _AerialTint;
            half4 _MistColor;
            float _MistHeight;
            half _MistStrength;
            float4 _Squeeze;
        CBUFFER_END

        // Where a virtual point is drawn: same direction from the camera, distance squeezed past _Squeeze.x.
        float3 MountainSqueeze(float3 virtualWS)
        {
            float3 camera = GetCameraPositionWS();
            float3 view = virtualWS - camera;
            float distance = max(length(view), 1e-3);
            float squeezed = distance <= _Squeeze.x ? distance : _Squeeze.x + _Squeeze.y * log(1.0 + (distance - _Squeeze.x) / _Squeeze.y);
            return camera + view * (squeezed / distance);
        }
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

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 virtualWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half4 color : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.virtualWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(MountainSqueeze(output.virtualWS));
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 p = input.virtualWS;
                float3 normal = normalize(input.normalWS);
                float3 view = p - GetCameraPositionWS();
                float distance = length(view);
                float3 viewDir = view / distance;
                half slope = 1.0 - normal.y;
                half cavity = input.color.b * 2.0 - 1.0;

                // Forest covers every slope and ledge up to about 60 degrees, as on the Phong Nha karst: kilometre
                // patches of lighter and darker canopy, crowns of emergent trees, darker wet ravines.
                half patches = EnvFbm(p.xz / 420.0);
                half crowns = EnvValueNoise(p.xz / 38.0) * 0.6 + EnvValueNoise(p.xz / 11.0) * 0.4;
                half3 canopy = lerp(_CanopyDark.rgb, _CanopyLight.rgb, saturate(patches * 1.3 - 0.15));
                canopy *= 0.78 + 0.44 * crowns;
                canopy *= 1.0 + 0.45 * clamp(cavity, -1.0, 0.6);
                // Bare limestone only on the cliff bands: pale grey-cream, with dark water stains running straight
                // down the face (noise stretched vertically, projected on the two vertical planes).
                half2 facing = abs(normal.xz) / max(abs(normal.x) + abs(normal.z), 1e-3);
                half stain = EnvFbm(float2(p.z / 9.0, p.y / 140.0)) * facing.x + EnvFbm(float2(p.x / 9.0, p.y / 140.0)) * facing.y;
                half3 rock = _Rock.rgb * (0.82 + 0.3 * EnvValueNoise(float2(p.x + p.z, p.y) / 35.0));
                rock = lerp(rock, _RockDark.rgb, smoothstep(0.48, 0.72, stain) * 0.9);
                // Only tall relief bares its rock (the low jungle ridges stay green however steep), and trees cling to
                // broken ledges across every cliff face: horizontal bands a few tens of metres high, too fine for the mesh.
                half cliff = smoothstep(_CliffSlope - 0.06, _CliffSlope + 0.06, slope + (EnvFbm(p.xz / 150.0) - 0.5) * 0.24);
                cliff *= smoothstep(_CliffHeight - 30.0, _CliffHeight + 30.0, p.y + (EnvValueNoise(p.xz / 90.0) - 0.5) * 60.0);
                half ledge = EnvValueNoise(float2((p.x + p.z) / 240.0, p.y / _Ledges.x)) * 0.7 + EnvValueNoise(p.xz / 60.0) * 0.3;
                cliff *= 1.0 - smoothstep(1.0 - _Ledges.y, 1.0 - _Ledges.y + 0.08, ledge);
                half3 albedo = lerp(canopy, rock, cliff);

                Light sun = GetMainLight();
                half sunVisible = input.color.r;
                half3 direct = sun.color * saturate(dot(normal, sun.direction)) * sunVisible;
                half3 ambient = SampleSH(normal) * input.color.g;
                half3 color = albedo * (direct + ambient);

                // Looking into the low sun: ridge lines and grazing slopes catch a bright rim of scattered light.
                // Only near-silhouette, convex faces (ridges), so whole slopes never wash out.
                half towards = pow(saturate(dot(viewDir, _EnvSunDir.xyz)), 4.0);
                half grazing = pow(1.0 - saturate(dot(normal, -viewDir)), 7.0);
                color += _EnvSkyGlow.rgb * grazing * towards * _Rim * (0.35 + 0.65 * sunVisible) * saturate(cavity * 3.0 + 0.2);

                // Valley mist pools between the ranges. Then aerial perspective over the true (unsqueezed) distance, so
                // each range steps paler: green-grey ridges at a kilometre, blue-grey towers at three, the far ranges
                // close to the horizon sky. The air is cooler than the horizon at middle distances and becomes the
                // horizon colour as it thickens; the atmosphere term finally swallows the farthest ranges.
                half3 haze = EnvHazeColor(viewDir);
                half mist = exp(-max(p.y, 0.0) / _MistHeight) * (1.0 - exp(-distance / 3500.0)) * _MistStrength;
                color = lerp(color, lerp(_MistColor.rgb, haze, 0.45), saturate(mist));
                half aerial = (1.0 - exp(-distance / _Aerial.x)) * _Aerial.y;
                half3 air = lerp(dot(haze, half3(0.2126, 0.7152, 0.0722)) * _AerialTint.rgb, haze, aerial * aerial);
                color = lerp(color, air, aerial);
                color = lerp(color, haze, EnvHazeAmount(distance, p.y));
                return half4(color, 1);
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
