using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TienTuyen.Presentation
{
    /// <summary>
    /// The faction flag a character carries on its back (Vietnam for the hero,
    /// the United States for enemies). The Blender catalog authors each flag
    /// colour as a separate "banner cloth" mesh cut into columns; they are merged
    /// into one mesh per character, one sub-mesh per material, and rippled away
    /// from the pole. Movement makes the flag trail and flutter harder.
    /// </summary>
    public sealed class CombatBanner
    {
        private const string ClothPrefix = "banner cloth", PoleName = "banner pole";
        private const float MaxTrailDegrees = 32f;

        private readonly Transform pivot;
        private readonly Quaternion restRotation;
        private readonly Vector3 restFly;
        private readonly Mesh mesh;
        private readonly Renderer renderer;
        private readonly Vector3[] rest, current;
        private readonly float length, seed;
        private float phase, trail, effort;

        public Transform Pivot => pivot;
        public Mesh Mesh => mesh;
        public Material[] Materials => renderer.sharedMaterials;
        /// <summary>Skip the vertex ripple while no camera sees the flag (default).</summary>
        public bool CullWhenHidden { get; set; } = true;

        private CombatBanner(Transform pivot, Mesh mesh, Renderer renderer, float seed)
        {
            this.pivot = pivot;
            this.mesh = mesh;
            this.renderer = renderer;
            this.seed = seed;
            restRotation = pivot.localRotation;
            restFly = restRotation * Vector3.right;
            rest = mesh.vertices;
            current = (Vector3[])rest.Clone();
            foreach (var vertex in rest) length = Mathf.Max(length, vertex.x);
            length = Mathf.Max(.05f, length);
            phase = seed;
        }

        /// <summary>
        /// Builds the banner under <paramref name="parent"/> (the chest pivot, so the
        /// flag leans with the body). Returns null when the model has no banner.
        /// </summary>
        public static CombatBanner Create(Transform parent, MeshRenderer[] renderers, float seed)
        {
            var cloth = new List<MeshRenderer>();
            MeshRenderer pole = null;
            foreach (var part in renderers)
            {
                if (part == null) continue;
                if (part.name == PoleName) pole = part;
                else if (part.name.StartsWith(ClothPrefix, System.StringComparison.Ordinal)) cloth.Add(part);
            }
            if (pole == null || cloth.Count == 0) return null;
            foreach (var part in cloth)
            {
                var filter = part.GetComponent<MeshFilter>();
                // A non-readable import keeps its static (unwaving) flag.
                if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable) return null;
            }

            // Pivot on the pole axis at the cloth's mid height; local +X runs from the
            // pole to the fly end and +Y is up, so vertex X is distance from the pole.
            Vector3 up = parent.up;
            Vector3 poleCenter = Center(pole);
            Vector3 clothCenter = Vector3.zero;
            foreach (var part in cloth) clothCenter += Center(part);
            clothCenter /= cloth.Count;
            Vector3 fly = Vector3.ProjectOnPlane(clothCenter - poleCenter, up);
            if (fly.sqrMagnitude < .0001f) return null;
            fly.Normalize();
            var pivot = new GameObject("BannerPivot").transform;
            pivot.SetParent(parent, false);
            pivot.position = poleCenter + up * Vector3.Dot(clothCenter - poleCenter, up);
            pivot.rotation = Quaternion.LookRotation(Vector3.Cross(fly, up), up);

            // Merge the cloth per material, then keep one sub-mesh per material.
            var groups = new Dictionary<Material, List<CombineInstance>>();
            var order = new List<Material>();
            Matrix4x4 toPivot = pivot.worldToLocalMatrix;
            foreach (var part in cloth)
            {
                var material = part.sharedMaterial;
                if (!groups.TryGetValue(material, out var list)) { groups[material] = list = new List<CombineInstance>(); order.Add(material); }
                list.Add(new CombineInstance { mesh = part.GetComponent<MeshFilter>().sharedMesh, transform = toPivot * part.localToWorldMatrix });
                part.enabled = false;
            }
            var merged = new CombineInstance[order.Count];
            for (int i = 0; i < order.Count; i++)
            {
                var piece = new Mesh { name = "BannerPiece" };
                piece.CombineMeshes(groups[order[i]].ToArray(), true, true);
                merged[i] = new CombineInstance { mesh = piece, transform = Matrix4x4.identity };
            }
            var mesh = new Mesh { name = "BannerCloth", hideFlags = HideFlags.DontSave };
            mesh.CombineMeshes(merged, false, true);
            foreach (var piece in merged) Object.Destroy(piece.mesh);
            mesh.MarkDynamic();
            // Generous fixed bounds: the ripple never leaves them, so no per-frame recompute.
            var bounds = mesh.bounds;
            bounds.Expand(new Vector3(.1f, .1f, .3f));
            mesh.bounds = bounds;

            var node = new GameObject("BannerCloth");
            node.transform.SetParent(pivot, false);
            node.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = node.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = order.ToArray();
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            var banner = new CombatBanner(pivot, mesh, renderer, seed);
            banner.Tick(0f, Vector3.zero);
            return banner;
        }

        /// <param name="localVelocity">Character velocity in the pose (body) frame.</param>
        public void Tick(float dt, Vector3 localVelocity)
        {
            localVelocity.y = 0;
            float speed = localVelocity.magnitude;
            float blend = 1f - Mathf.Exp(-4f * dt);
            effort = Mathf.Lerp(effort, Mathf.Clamp01(speed / 4.5f), blend);
            // Running pushes the fly end back, as if into a head wind.
            float targetTrail = 0f;
            if (speed > .2f)
            {
                Vector3 back = -localVelocity / speed;
                Vector3 flat = Vector3.ProjectOnPlane(restFly, Vector3.up).normalized;
                targetTrail = Mathf.Clamp(Vector3.SignedAngle(flat, back, Vector3.up), -MaxTrailDegrees, MaxTrailDegrees) * effort;
            }
            trail = Mathf.Lerp(trail, targetTrail, blend);
            pivot.localRotation = Quaternion.AngleAxis(trail, Vector3.up) * restRotation;

            phase += dt * (5.2f + effort * 4.5f);
            if (dt > 0f && CullWhenHidden && !renderer.isVisible) return;
            float amplitude = .035f + effort * .03f;
            for (int i = 0; i < rest.Length; i++)
            {
                Vector3 v = rest[i];
                float t = Mathf.Clamp01(v.x / length);
                float wave = Mathf.Sin(v.x * 10.5f - phase + v.y * 2.2f) + .35f * Mathf.Sin(v.x * 23f - phase * 1.7f + seed);
                float lift = Mathf.Sin(v.x * 7f - phase * .8f + seed) * .012f * t;
                current[i] = new Vector3(v.x - Mathf.Abs(wave) * .012f * t, v.y + lift - t * t * .025f * (1f - effort), v.z + wave * amplitude * t);
            }
            mesh.vertices = current;
            mesh.RecalculateNormals();
        }

        public void Dispose()
        {
            if (mesh != null) Object.Destroy(mesh);
        }

        private static Vector3 Center(MeshRenderer renderer)
        {
            var filter = renderer.GetComponent<MeshFilter>();
            return filter != null && filter.sharedMesh != null ? renderer.transform.TransformPoint(filter.sharedMesh.bounds.center) : renderer.transform.position;
        }
    }
}
