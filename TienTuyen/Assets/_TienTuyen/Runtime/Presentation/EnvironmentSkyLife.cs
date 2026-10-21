using UnityEngine;
using UnityEngine.Rendering;

namespace TienTuyen.Presentation
{
    /// <summary>
    /// Life in the far air over the backdrop: shafts of sun falling through the cloud onto the valleys beyond 500 m
    /// (EnvFarLightShafts), and flocks circling the near towers and gliding over the river (EnvBirds). Both are one
    /// static mesh each; the shaders shape them from the sun's direction and the clock (EnvSkyLife.hlsl).
    /// </summary>
    internal static class EnvironmentSkyLife
    {
        /// <summary>Where a shaft comes down: bearing from the station (deg), distance (m), half width (m), cloud height (m).</summary>
        private readonly struct Shaft
        {
            public readonly float Bearing, Distance, HalfWidth, Cloud;
            public Shaft(float bearing, float distance, float halfWidth, float cloud) { Bearing = bearing; Distance = distance; HalfWidth = halfWidth; Cloud = cloud; }
        }

        /// <summary>Most towards the sun's side (south-west), where they shine brightest; some down each reach of the gorge and to the south-east.</summary>
        private static readonly Shaft[] Shafts =
        {
            new Shaft(195f, 1400f, 100f, 650f), new Shaft(208f, 1900f, 150f, 700f), new Shaft(222f, 1600f, 87.5f, 600f),
            new Shaft(232f, 2600f, 200f, 800f), new Shaft(215f, 3200f, 225f, 850f), new Shaft(245f, 2100f, 125f, 700f),
            new Shaft(185f, 2400f, 137.5f, 750f), new Shaft(170f, 1800f, 87.5f, 650f), new Shaft(255f, 3000f, 175f, 800f),
            new Shaft(200f, 4200f, 275f, 900f), new Shaft(285f, 2600f, 150f, 750f), new Shaft(80f, 2600f, 150f, 750f),
            new Shaft(150f, 2200f, 150f, 750f), new Shaft(160f, 3000f, 200f, 850f), new Shaft(140f, 1700f, 112.5f, 650f),
            new Shaft(100f, 3200f, 200f, 850f), new Shaft(300f, 3400f, 200f, 850f),
        };

        /// <summary>A flock: centre of its circles, birds, radius range (m), angular speed (rad/s, sign the way round), half span (m), plumage.</summary>
        private readonly struct Flock
        {
            public readonly Vector3 Centre;
            public readonly int Count;
            public readonly float RadiusMin, RadiusMax, Speed, HalfSpan;
            public readonly Color32 Plumage;
            public Flock(Vector3 centre, int count, float radiusMin, float radiusMax, float speed, float halfSpan, Color32 plumage)
            {
                Centre = centre; Count = count; RadiusMin = radiusMin; RadiusMax = radiusMax; Speed = speed; HalfSpan = halfSpan; Plumage = plumage;
            }
        }

        /// <summary>
        /// Kites round the nearest towers of the gorge mouth (build_mountains.py NEAR), egrets over the river's west
        /// reach, and a few raptors soaring high to the east. Larger than life, so they read at their distance.
        /// </summary>
        private static readonly Flock[] Flocks =
        {
            new Flock(new Vector3(184f, 150f, -737f), 14, 80f, 140f, .25f, 1.4f, new Color32(16, 14, 13, 255)),
            new Flock(new Vector3(460f, 190f, -865f), 6, 140f, 220f, .15f, 1.6f, new Color32(20, 17, 15, 255)),
            new Flock(new Vector3(-420f, 30f, -240f), 8, 120f, 180f, -.12f, 1.2f, new Color32(225, 222, 210, 255)),
            new Flock(new Vector3(900f, 260f, 0f), 3, 200f, 260f, -.08f, 1.8f, new Color32(40, 30, 22, 255)),
        };

        /// <summary>One bird: body front and back, wing tips (x span, y lift, z forward, in half spans).</summary>
        private static readonly Vector3[] Bird = { new Vector3(0f, 0f, .6f), new Vector3(0f, 0f, -.5f), new Vector3(-1f, 0f, -.15f), new Vector3(1f, 0f, -.15f) };

        private const int ShaftRows = 6;

        public static void Build(Transform parent, Vector4 squeeze)
        {
            var shafts = Shader.Find("TienTuyen/Environment/FarLightShafts");
            if (shafts != null && shafts.isSupported)
            {
                var material = new Material(shafts) { name = "Env_FarLightShafts" };
                material.SetVector("_Squeeze", squeeze);
                Spawn(parent, "FarLightShafts", ShaftMesh(), material);
            }
            var birds = Shader.Find("TienTuyen/Environment/Birds");
            if (birds != null && birds.isSupported)
            {
                var material = new Material(birds) { name = "Env_Birds" };
                material.SetVector("_Squeeze", squeeze);
                Spawn(parent, "Birds", BirdMesh(), material);
            }
        }

        private static Mesh ShaftMesh()
        {
            int per = ShaftRows * 2;
            var vertices = new Vector3[Shafts.Length * per];
            var normals = new Vector3[vertices.Length];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[Shafts.Length * (ShaftRows - 1) * 6];
            int t = 0;
            for (int k = 0; k < Shafts.Length; k++)
            {
                var s = Shafts[k];
                float a = s.Bearing * Mathf.Deg2Rad;
                // Anchored below the ground: the ground and the ranges hide the foot, the shader fades it in.
                var anchor = new Vector3(Mathf.Sin(a) * s.Distance, -20f, Mathf.Cos(a) * s.Distance);
                for (int row = 0; row < ShaftRows; row++)
                for (int side = 0; side < 2; side++)
                {
                    int v = k * per + row * 2 + side;
                    vertices[v] = anchor;
                    normals[v] = new Vector3(s.HalfWidth, s.Cloud, k * .618f % 1f * 10f);
                    uv[v] = new Vector2(side * 2f - 1f, row / (ShaftRows - 1f));
                    if (row == ShaftRows - 1 || side == 1) continue;
                    triangles[t++] = v; triangles[t++] = v + 2; triangles[t++] = v + 1;
                    triangles[t++] = v + 1; triangles[t++] = v + 2; triangles[t++] = v + 3;
                }
            }
            var mesh = new Mesh { name = "Env_FarLightShafts", vertices = vertices, normals = normals, uv = uv, triangles = triangles };
            mesh.bounds = Everywhere;
            mesh.UploadMeshData(true);
            return mesh;
        }

        private static Mesh BirdMesh()
        {
            int count = 0;
            foreach (var f in Flocks) count += f.Count;
            var vertices = new Vector3[count * Bird.Length];
            var normals = new Vector3[vertices.Length];
            var tangents = new Vector4[vertices.Length];
            var colors = new Color32[vertices.Length];
            var triangles = new int[count * 6];
            var random = new System.Random(31);
            int b = 0;
            foreach (var f in Flocks)
            {
                // Loosely together: close phases, scattered radii and heights.
                float phase = (float)random.NextDouble() * Mathf.PI * 2f;
                for (int i = 0; i < f.Count; i++, b++)
                {
                    var centre = f.Centre + new Vector3(0f, ((float)random.NextDouble() - .5f) * 24f, 0f);
                    float radius = Mathf.Lerp(f.RadiusMin, f.RadiusMax, (float)random.NextDouble());
                    var motion = new Vector4(radius, f.Speed * f.RadiusMin / radius * (.9f + .2f * (float)random.NextDouble()),
                        phase + i * .11f + ((float)random.NextDouble() - .5f) * .2f, f.HalfSpan * (.85f + .3f * (float)random.NextDouble()));
                    for (int c = 0; c < Bird.Length; c++)
                    {
                        int v = b * Bird.Length + c;
                        vertices[v] = centre;
                        normals[v] = Bird[c];
                        tangents[v] = motion;
                        colors[v] = f.Plumage;
                    }
                    int o = b * Bird.Length;
                    triangles[b * 6] = o; triangles[b * 6 + 1] = o + 1; triangles[b * 6 + 2] = o + 2;
                    triangles[b * 6 + 3] = o; triangles[b * 6 + 4] = o + 3; triangles[b * 6 + 5] = o + 1;
                }
            }
            var mesh = new Mesh { name = "Env_Birds", vertices = vertices, normals = normals, tangents = tangents, colors32 = colors, triangles = triangles };
            mesh.bounds = Everywhere;
            mesh.UploadMeshData(true);
            return mesh;
        }

        /// <summary>The shaders move every vertex: never cull these.</summary>
        private static readonly Bounds Everywhere = new Bounds(Vector3.zero, Vector3.one * 20000f);

        private static void Spawn(Transform parent, string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }
    }
}
