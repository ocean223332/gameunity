using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TienTuyen.Presentation
{
    /// <summary>
    /// Mesh parts of the prop catalog in Resources/Models (see Art/Source/README.md). A model's parts
    /// are its named meshes, each with its imported materials; "&lt;part&gt;_LOD1" is the distant
    /// version of "&lt;part&gt;". Part matrices put Blender's (x, y, z) at (x, z, y), the frame station
    /// layouts are written in: CombatArtDirector.AttachImportedModel's X -90 turn alone leaves
    /// Blender +Y (muzzles, the bunker's back wall) at -Z, so a half turn about Y follows it.
    /// </summary>
    internal static class PropLibrary
    {
        internal sealed class Part
        {
            public Mesh Mesh;
            public Material[] Materials;
            public Matrix4x4 Local;
            public Part Lod1;
        }

        private static readonly Matrix4x4 ZUp = Matrix4x4.Rotate(Quaternion.Euler(0f, 180f, 0f) * Quaternion.Euler(-90f, 0f, 0f));
        private static readonly Dictionary<string, Dictionary<string, Part>> Models =
            new Dictionary<string, Dictionary<string, Part>>(StringComparer.Ordinal);

        public static Part Get(string model, string part)
        {
            if (!Models.TryGetValue(model, out var parts)) Models[model] = parts = Load(model);
            if (parts != null && parts.TryGetValue(part, out var found)) return found;
            if (parts != null) Debug.LogWarning("Prop part not found: Models/" + model + " " + part);
            return null;
        }

        private static Dictionary<string, Part> Load(string model)
        {
            var prefab = Resources.Load<GameObject>("Models/" + model);
            if (prefab == null)
            {
                Debug.LogWarning("Art model not found: Models/" + model);
                return null;
            }
            var parts = new Dictionary<string, Part>(StringComparer.Ordinal);
            var distant = new Dictionary<string, Part>(StringComparer.Ordinal);
            foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (filter.sharedMesh == null || renderer == null) continue;
                // The model root's own transform is replaced by the Z-up turn, as for attached models.
                var local = Matrix4x4.identity;
                for (var node = filter.transform; node != prefab.transform; node = node.parent)
                    local = Matrix4x4.TRS(node.localPosition, node.localRotation, node.localScale) * local;
                var part = new Part { Mesh = filter.sharedMesh, Materials = renderer.sharedMaterials, Local = ZUp * local };
                string name = filter.name;
                if (name.EndsWith("_LOD1", StringComparison.Ordinal)) distant[name.Substring(0, name.Length - 5)] = part;
                else parts[name] = part;
            }
            foreach (var pair in distant)
                if (parts.TryGetValue(pair.Key, out var near)) near.Lod1 = pair.Value;
            return parts;
        }
    }

    /// <summary>
    /// Static prop instances merged per cluster (one emplacement, one building and its stores) into a
    /// LOD0 and a LOD1 mesh with one submesh per material, under a LODGroup.
    /// </summary>
    internal sealed class PropBatch
    {
        private sealed class Cluster
        {
            public string Name;
            public readonly Dictionary<Material, List<CombineInstance>> Near = new Dictionary<Material, List<CombineInstance>>();
            public readonly Dictionary<Material, List<CombineInstance>> Far = new Dictionary<Material, List<CombineInstance>>();
        }

        // Screen heights at which a cluster switches to LOD1 and is culled.
        private const float Lod1Height = .12f, CullHeight = .004f;

        private readonly Transform parent;
        private readonly List<Cluster> clusters = new List<Cluster>();
        private Cluster current;

        public PropBatch(Transform parent) { this.parent = parent; }

        public void Begin(string name)
        {
            current = new Cluster { Name = name };
            clusters.Add(current);
        }

        /// <summary>Adds a model part whose catalog root sits at <paramref name="world"/>; false if the part is missing.</summary>
        public bool Add(string model, string part, Matrix4x4 world)
        {
            var near = PropLibrary.Get(model, part);
            if (near == null) return false;
            if (current == null) Begin(model);
            Collect(current.Near, near, world);
            Collect(current.Far, near.Lod1 ?? near, world);
            return true;
        }

        private static void Collect(Dictionary<Material, List<CombineInstance>> groups, PropLibrary.Part part, Matrix4x4 world)
        {
            var transform = world * part.Local;
            for (int s = 0; s < part.Mesh.subMeshCount; s++)
            {
                var material = part.Materials[Mathf.Min(s, part.Materials.Length - 1)];
                if (material == null) continue;
                if (!groups.TryGetValue(material, out var list)) groups[material] = list = new List<CombineInstance>();
                list.Add(new CombineInstance { mesh = part.Mesh, subMeshIndex = s, transform = transform });
            }
        }

        public void Emit()
        {
            foreach (var cluster in clusters)
            {
                if (cluster.Near.Count == 0) continue;
                var root = new GameObject(cluster.Name).transform;
                root.SetParent(parent, false);
                var near = MakeRenderer(root, cluster.Name + "_LOD0", cluster.Near);
                var far = MakeRenderer(root, cluster.Name + "_LOD1", cluster.Far);
                var lods = root.gameObject.AddComponent<LODGroup>();
                lods.SetLODs(new[] { new LOD(Lod1Height, new[] { near }), new LOD(CullHeight, new[] { far }) });
                lods.RecalculateBounds();
            }
            clusters.Clear();
            current = null;
        }

        private static Renderer MakeRenderer(Transform root, string name, Dictionary<Material, List<CombineInstance>> groups)
        {
            var materials = new Material[groups.Count];
            var merged = new CombineInstance[groups.Count];
            int index = 0, vertices = 0;
            foreach (var pair in groups)
            {
                var piece = new Mesh { indexFormat = IndexFormat.UInt32 };
                piece.CombineMeshes(pair.Value.ToArray(), true, true);
                vertices += piece.vertexCount;
                materials[index] = pair.Key;
                merged[index++] = new CombineInstance { mesh = piece };
            }
            var mesh = new Mesh { name = name, indexFormat = vertices > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.CombineMeshes(merged, false, false);
            foreach (var piece in merged)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(piece.mesh);
                else UnityEngine.Object.DestroyImmediate(piece.mesh);
            }
            mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            return renderer;
        }
    }
}
