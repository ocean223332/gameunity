using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TienTuyen.Presentation
{
    /// <summary>One placed plant: a FloraBiome species at a position, turned and scaled.</summary>
    public struct FloraInstance
    {
        public int Species;
        public Vector3 Position;
        public float Yaw, Scale;
    }

    /// <summary>
    /// Draws the placed FloraBiome prefabs with GPU instancing instead of thousands of GameObjects: the
    /// prefabs' LODGroups give the meshes, materials and switch heights, each plant picks its LOD from its
    /// screen height like a LODGroup would, and whole cells outside the view are skipped. No colliders exist;
    /// the plants are only drawn. Shadows come from LOD0 and LOD1, never from the cross-cards.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public sealed class FloraScatter : MonoBehaviour
    {
        private const float CellSize = 20f;

        private sealed class Part
        {
            public Mesh Mesh;
            public int Submesh;
            public Material Material;
        }

        private sealed class Kind
        {
            public Part[][] Lods;          // per LOD level
            public float[] Transitions;    // screen-relative height below which the level is left
            public float Size;             // LODGroup size at scale 1
            public float[] InstanceSize;   // per placed instance: Size * scale
            // Per LOD level: drawn and casting shadows, drawn only, casting shadows only (just out of view).
            public List<Matrix4x4>[] Lit, Unshadowed, ShadowOnly;
        }

        private sealed class Cell
        {
            public Bounds Bounds;
            public readonly List<int> Instances = new List<int>();
        }

        private FloraBiome biome;
        private FloraInstance[] instances;
        private Matrix4x4[] matrices;
        private Kind[] kinds;
        private Cell[] cells;
        private Bounds worldBounds;
        private readonly Plane[] planes = new Plane[6];
        private Vector3 lastPosition = new Vector3(float.NaN, 0, 0);
        private Quaternion lastRotation;
        private float lastFov;

        public IReadOnlyList<FloraInstance> Instances => instances;
        public FloraBiome Biome => biome;

        internal static FloraScatter Create(Transform parent, FloraBiome biome, List<FloraInstance> placed)
        {
            var go = new GameObject("Flora");
            go.transform.SetParent(parent, false);
            var scatter = go.AddComponent<FloraScatter>();
            scatter.Initialize(biome, placed);
            return scatter;
        }

        /// <summary>Height of a placed plant in metres.</summary>
        public float HeightOf(FloraInstance plant) => biome.Plants[plant.Species].Height * plant.Scale;

        private void Initialize(FloraBiome source, List<FloraInstance> placed)
        {
            biome = source;
            instances = placed.ToArray();
            kinds = new Kind[biome.Plants.Length];
            for (int i = 0; i < kinds.Length; i++) kinds[i] = Describe(biome.Plants[i].Prefab);

            matrices = new Matrix4x4[instances.Length];
            var byCell = new Dictionary<Vector2Int, Cell>();
            for (int i = 0; i < instances.Length; i++)
            {
                var p = instances[i];
                matrices[i] = Matrix4x4.TRS(p.Position, Quaternion.Euler(0, p.Yaw, 0), Vector3.one * p.Scale);
                var plant = biome.Plants[p.Species];
                var bounds = new Bounds(p.Position + Vector3.up * plant.Height * p.Scale * .5f,
                    new Vector3(plant.Radius * 2f, plant.Height, plant.Radius * 2f) * p.Scale);
                var key = new Vector2Int(Mathf.FloorToInt(p.Position.x / CellSize), Mathf.FloorToInt(p.Position.z / CellSize));
                if (!byCell.TryGetValue(key, out var cell)) byCell.Add(key, cell = new Cell { Bounds = bounds });
                cell.Bounds.Encapsulate(bounds);
                cell.Instances.Add(i);
                if (i == 0) worldBounds = bounds; else worldBounds.Encapsulate(bounds);
            }
            cells = new Cell[byCell.Count];
            byCell.Values.CopyTo(cells, 0);
            foreach (var kind in kinds)
            {
                if (kind == null) continue;
                kind.InstanceSize = new float[instances.Length];
            }
            for (int i = 0; i < instances.Length; i++)
            {
                var kind = kinds[instances[i].Species];
                if (kind != null) kind.InstanceSize[i] = kind.Size * instances[i].Scale;
            }
        }

        private static Kind Describe(GameObject prefab)
        {
            if (prefab == null) return null;
            var group = prefab.GetComponent<LODGroup>();
            if (group == null) return null;
            var lods = group.GetLODs();
            var kind = new Kind
            {
                Lods = new Part[lods.Length][],
                Transitions = new float[lods.Length],
                Size = group.size,
                Lit = new List<Matrix4x4>[lods.Length],
                Unshadowed = new List<Matrix4x4>[lods.Length],
                ShadowOnly = new List<Matrix4x4>[lods.Length]
            };
            for (int l = 0; l < lods.Length; l++)
            {
                var parts = new List<Part>();
                foreach (var renderer in lods[l].renderers)
                {
                    if (renderer == null) continue;
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null) continue;
                    var materials = renderer.sharedMaterials;
                    for (int s = 0; s < filter.sharedMesh.subMeshCount && s < materials.Length; s++)
                        parts.Add(new Part { Mesh = filter.sharedMesh, Submesh = s, Material = materials[s] });
                }
                kind.Lods[l] = parts.ToArray();
                kind.Transitions[l] = lods[l].screenRelativeTransitionHeight;
                kind.Lit[l] = new List<Matrix4x4>(256);
                kind.Unshadowed[l] = new List<Matrix4x4>(256);
                kind.ShadowOnly[l] = new List<Matrix4x4>(64);
            }
            return kind;
        }

        private void Update()
        {
            var view = Camera.main;
            if (view == null || kinds == null) return;
            var t = view.transform;
            if ((t.position - lastPosition).sqrMagnitude > .04f || Quaternion.Angle(t.rotation, lastRotation) > .75f ||
                !Mathf.Approximately(view.fieldOfView, lastFov))
            {
                lastPosition = t.position; lastRotation = t.rotation; lastFov = view.fieldOfView;
                Select(view);
            }
            Submit();
        }

        /// <summary>Sorts the plants in view (and those near enough to throw shadows into it) into their LODs.</summary>
        private void Select(Camera view)
        {
            foreach (var kind in kinds)
                if (kind != null)
                    for (int l = 0; l < kind.Lit.Length; l++)
                    {
                        kind.Lit[l].Clear(); kind.Unshadowed[l].Clear(); kind.ShadowOnly[l].Clear();
                    }
            GeometryUtility.CalculateFrustumPlanes(view, planes);
            Vector3 eye = view.transform.position;
            // Beyond the pipeline's shadow distance nothing casts, so far plants skip the four cascade passes.
            float shadowReach = GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp ? urp.shadowDistance : QualitySettings.shadowDistance;
            float perMetre = 1f / (2f * Mathf.Tan(view.fieldOfView * .5f * Mathf.Deg2Rad)) * QualitySettings.lodBias;
            foreach (var cell in cells)
            {
                bool seen = GeometryUtility.TestPlanesAABB(planes, cell.Bounds);
                if (!seen && cell.Bounds.SqrDistance(eye) > shadowReach * shadowReach) continue;
                foreach (int i in cell.Instances)
                {
                    var kind = kinds[instances[i].Species];
                    if (kind == null) continue;
                    float distance = Mathf.Max(Vector3.Distance(eye, instances[i].Position), .01f);
                    float screen = kind.InstanceSize[i] * perMetre / distance;
                    int level = 0;
                    while (level < kind.Transitions.Length && screen < kind.Transitions[level]) level++;
                    if (level >= kind.Transitions.Length) continue;   // culled, like a LODGroup past its last level
                    // The cross-cards never cast shadows.
                    bool shadows = level < kind.Transitions.Length - 1 && distance < shadowReach;
                    if (seen) (shadows ? kind.Lit : kind.Unshadowed)[level].Add(matrices[i]);
                    else if (shadows) kind.ShadowOnly[level].Add(matrices[i]);
                }
            }
        }

        private void Submit()
        {
            for (int k = 0; k < kinds.Length; k++)
            {
                var kind = kinds[k];
                if (kind == null) continue;
                for (int l = 0; l < kind.Lods.Length; l++)
                {
                    Draw(kind.Lods[l], kind.Lit[l], ShadowCastingMode.On);
                    Draw(kind.Lods[l], kind.Unshadowed[l], ShadowCastingMode.Off);
                    Draw(kind.Lods[l], kind.ShadowOnly[l], ShadowCastingMode.ShadowsOnly);
                }
            }
        }

        private void Draw(Part[] parts, List<Matrix4x4> list, ShadowCastingMode shadows)
        {
            if (list.Count == 0) return;
            foreach (var part in parts)
            {
                var rp = new RenderParams(part.Material)
                {
                    worldBounds = worldBounds,
                    layer = gameObject.layer,
                    shadowCastingMode = shadows,
                    receiveShadows = true,
                    lightProbeUsage = LightProbeUsage.BlendProbes,
                    motionVectorMode = MotionVectorGenerationMode.ForceNoMotion
                };
                for (int start = 0; start < list.Count; start += 1023)
                    Graphics.RenderMeshInstanced(rp, part.Mesh, part.Submesh, list, Mathf.Min(1023, list.Count - start), start);
            }
        }
    }
}
