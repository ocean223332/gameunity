using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using TienTuyen.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace TienTuyen.Editor
{
    /// <summary>
    /// Imports the plant species exported by Art/Source flora scripts into Art/Flora/&lt;Species&gt;/ (an FBX
    /// with _LOD0, _LOD1 and a _LOD2 cross-card, textures and a JSON description) and builds from them the
    /// Leaf materials, one prefab per species with a tuned LODGroup and no colliders, and the FloraBiome
    /// asset FloraScatter loads at runtime. Run Tien Tuyen/Build Flora after changing an export.
    /// </summary>
    public sealed class FloraPrefabBuilder : AssetPostprocessor
    {
        private const string FloraFolder = "Assets/_TienTuyen/Art/Flora/";
        private const string PrefabFolder = FloraFolder + "Prefabs";
        private const string BiomePath = "Assets/_TienTuyen/Art/Resources/" + FloraBiome.ResourcePath + ".asset";
        // The shoulder camera's FOV, so the LOD distances below hold in game.
        private const float DesignFieldOfView = 56f;

        [Serializable]
        private sealed class SpeciesJson
        {
            public string name;
            public MaterialJson[] materials;
            public float height, radius;
            public int[] tris;
        }

        [Serializable]
        private sealed class MaterialJson
        {
            public string name;
            public bool leaf, cutout, normal, card;
        }

        private bool InFlora => assetPath.StartsWith(FloraFolder, StringComparison.Ordinal);

        private void OnPreprocessModel()
        {
            if (!InFlora) return;
            var model = (ModelImporter)assetImporter;
            model.globalScale = 1f;
            model.useFileScale = true;
            model.importCameras = false;
            model.importLights = false;
            model.importVisibility = false;
            model.addCollider = false;
            model.importNormals = ModelImporterNormals.Import;
            model.importTangents = ModelImporterTangents.CalculateMikk;
            model.importBlendShapes = false;
            model.importAnimation = false;
            model.animationType = ModelImporterAnimationType.None;
            model.isReadable = false;
            model.meshCompression = ModelImporterMeshCompression.Off;
            model.generateSecondaryUV = false;
        }

        [MenuItem("Tien Tuyen/Build Flora")]
        public static void BuildAll()
        {
            if (!AssetDatabase.IsValidFolder(PrefabFolder)) AssetDatabase.CreateFolder(FloraFolder.TrimEnd('/'), "Prefabs");
            var plants = new List<FloraBiome.Species>();
            foreach (string folder in AssetDatabase.GetSubFolders(FloraFolder.TrimEnd('/')))
            {
                string name = Path.GetFileName(folder);
                string jsonPath = folder + "/" + name + ".json";
                if (!File.Exists(jsonPath)) continue;
                var json = JsonUtility.FromJson<SpeciesJson>(File.ReadAllText(jsonPath));
                var prefab = BuildSpecies(folder, json);
                if (prefab == null) continue;
                plants.Add(new FloraBiome.Species { Name = json.name, Prefab = prefab, Height = json.height, Radius = json.radius });
            }

            var biome = AssetDatabase.LoadAssetAtPath<FloraBiome>(BiomePath);
            if (biome == null)
            {
                biome = ScriptableObject.CreateInstance<FloraBiome>();
                AssetDatabase.CreateAsset(biome, BiomePath);
            }
            biome.Plants = plants.ToArray();
            EditorUtility.SetDirty(biome);
            AssetDatabase.SaveAssets();
            Debug.Log($"[FloraPrefabBuilder] Built {plants.Count} flora prefabs into {PrefabFolder}.");
        }

        private static GameObject BuildSpecies(string folder, SpeciesJson json)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(folder + "/" + json.name + ".fbx");
            if (model == null)
            {
                Debug.LogError($"[FloraPrefabBuilder] Missing {folder}/{json.name}.fbx");
                return null;
            }
            var materials = new Dictionary<string, Material>();
            foreach (var m in json.materials) materials[m.name] = BuildMaterial(folder, json, m);

            var instance = UnityEngine.Object.Instantiate(model);
            instance.name = json.name;
            try
            {
                foreach (var collider in instance.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
                foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var shared = renderer.sharedMaterials;
                    for (int i = 0; i < shared.Length; i++)
                        if (shared[i] != null && materials.TryGetValue(BlenderName(shared[i].name), out var replacement)) shared[i] = replacement;
                    renderer.sharedMaterials = shared;
                    renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                    renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                }

                var group = instance.GetComponent<LODGroup>();
                if (group == null) group = instance.AddComponent<LODGroup>();
                var renderers = new Renderer[3][];
                for (int l = 0; l < 3; l++)
                {
                    var child = instance.transform.Find(json.name + "_LOD" + l);
                    if (child == null) throw new InvalidOperationException($"{json.name} has no _LOD{l} mesh");
                    renderers[l] = new Renderer[] { child.GetComponent<MeshRenderer>() };
                }
                // The cross-card is two quads; its shadow would be a flat smear.
                renderers[2][0].shadowCastingMode = ShadowCastingMode.Off;
                group.RecalculateBounds();
                var distances = LodDistances(json.height);
                var lods = new LOD[3];
                for (int l = 0; l < 3; l++) lods[l] = new LOD(ScreenHeight(group.size, distances[l]), renderers[l]);
                group.SetLODs(lods);
                group.fadeMode = LODFadeMode.None;

                var prefab = PrefabUtility.SaveAsPrefabAsset(instance, PrefabFolder + "/" + json.name + ".prefab");
                return prefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        /// <summary>Blender suffixes copied materials ".001"; the LODs share one material per name.</summary>
        private static string BlenderName(string name) => Regex.Replace(name, @"\.\d+$", "");

        /// <summary>
        /// How far (m) each level is kept in game: small plants vanish in the litter, trees stay meshes across
        /// the playable forest and become cards only far out in the haze.
        /// </summary>
        private static float[] LodDistances(float height)
        {
            if (height < FloraBiome.TallHeight) return new[] { 14f, 35f, 75f };
            if (height < 7f) return new[] { 16f, 50f, 220f };
            return new[] { 28f, 140f, 1200f };
        }

        private static float ScreenHeight(float size, float distance) =>
            size * QualitySettings.lodBias / (2f * Mathf.Tan(DesignFieldOfView * .5f * Mathf.Deg2Rad) * distance);

        private static Material BuildMaterial(string folder, SpeciesJson species, MaterialJson m)
        {
            string path = folder + "/" + m.name + ".mat";
            var shader = Shader.Find("TienTuyen/Environment/Leaf");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.enableInstancing = true;
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/" + m.name + "_Albedo.png"));
            var normal = m.normal ? AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/" + m.name + "_Normal.png") : null;
            material.SetTexture("_BumpMap", normal);
            SetToggle(material, "_UseNormalMap", "_NORMALMAP", normal != null);
            SetToggle(material, "_AlphaClip", "_ALPHATEST_ON", m.cutout);
            material.SetFloat("_Cutoff", m.card ? .5f : .45f);
            material.SetFloat("_Cull", (float)(m.leaf ? CullMode.Off : CullMode.Back));

            material.SetFloat("_Flutter", m.leaf && !m.card ? .04f : 0f);
            material.SetFloat("_Smoothness", m.leaf ? .25f : .12f);
            material.SetFloat("_TintVariation", m.leaf ? .18f : .06f);
            // The canopy twig atlas is a light lime next to the scanned plants and glows when backlit.
            bool canopy = species.name.StartsWith("CanopyTree", StringComparison.Ordinal) && m.leaf;
            material.SetColor("_BaseColor", canopy ? new Color(.42f, .5f, .36f) : Color.white);
            material.SetFloat("_Translucency", !m.leaf ? 0f : canopy || m.card ? .45f : .8f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void SetToggle(Material material, string property, string keyword, bool on)
        {
            material.SetFloat(property, on ? 1f : 0f);
            if (on) material.EnableKeyword(keyword); else material.DisableKeyword(keyword);
        }
    }
}
