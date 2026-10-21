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
            // Leaf look from the source pack's own materials (flora_build EXT look); 0 keeps the house default.
            public float cutoff, smoothness, translucency, tintVariation;
            // The impostor render came out empty (flora_build): keep LOD1 out to the card's distance instead.
            public bool noCard;
            // Kept as far as the tall trees whatever its height (vine curtains on the karst wall, seen across the valley).
            public bool farLod;
            // With noCard: a species whose card stands in past StandInDistance, scaled to this one's height.
            public string cardFrom;
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
                bool standIn = json.noCard && !string.IsNullOrEmpty(json.cardFrom) && BorrowCard(folder, json, renderers[2][0]);
                if (json.noCard && !standIn) UnityEngine.Object.DestroyImmediate(renderers[2][0].gameObject);
                group.RecalculateBounds();
                var distances = LodDistances(json.farLod ? Mathf.Max(json.height, 7f) : json.height);
                if (standIn) distances[1] = StandInDistance;
                // Rocks keep their mesh until they cull: a rock's cross-card is a pale flat slab, and the dither between the
                // two read as a see-through stone. Big ones stay to the far wall's foot (at the outcrop class's 220 m they
                // dithered out in the river and overview shots; there are only a few dozen of them).
                bool rock = json.name.StartsWith("Rock_") || json.name.StartsWith("Outcrop_") || json.name == "RockFace";
                if (json.name == "RockFace" || json.name.StartsWith("Outcrop_")) distances[2] = BigRockDistance;
                if (rock) distances[1] = distances[2] * .98f;
                var lods = new LOD[json.noCard && !standIn ? 2 : 3];
                for (int l = 0; l < lods.Length; l++)
                    lods[l] = new LOD(ScreenHeight(group.size, distances[lods.Length == 2 && l == 1 ? 2 : l]), renderers[l]);
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

        // A borrowed card is another tree's silhouette, so it starts where the haze hides the difference; full LOD1s
        // out to 1200 m cost the forest segment over 1 ms GPU.
        private const float StandInDistance = 350f;
        private const float BigRockDistance = 600f;

        /// <summary>Gives <paramref name="card"/> the card mesh and material of json.cardFrom, scaled to json's height.</summary>
        private static bool BorrowCard(string folder, SpeciesJson json, Renderer card)
        {
            string other = folder.Substring(0, folder.LastIndexOf('/') + 1) + json.cardFrom;
            var text = AssetDatabase.LoadAssetAtPath<TextAsset>(other + "/" + json.cardFrom + ".json");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(other + "/" + json.cardFrom + ".fbx");
            var source = model != null ? model.transform.Find(json.cardFrom + "_LOD2") : null;
            if (text == null || source == null)
            {
                Debug.LogError($"[FloraPrefabBuilder] {json.name}: no card to borrow from {other}");
                return false;
            }
            var donor = JsonUtility.FromJson<SpeciesJson>(text.text);
            var cardJson = Array.Find(donor.materials, m => m.card);
            card.GetComponent<MeshFilter>().sharedMesh = source.GetComponent<MeshFilter>().sharedMesh;
            card.sharedMaterial = BuildMaterial(other, donor, cardJson);
            card.transform.localRotation = source.localRotation;
            card.transform.localScale = source.localScale * (json.height / donor.height);
            return true;
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
            // Trees turn to cards from 60 m: the forest is dense, and full fir LOD1s cost up to 4.5k triangles each.
            return new[] { 28f, 60f, 1200f };
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
            bool own = m.leaf && !m.card;
            material.SetFloat("_Cutoff", m.card ? .5f : own && species.cutoff > 0f ? species.cutoff : .45f);
            material.SetFloat("_Cull", (float)(m.leaf ? CullMode.Off : CullMode.Back));

            material.SetFloat("_Flutter", m.leaf && !m.card ? .04f : 0f);
            material.SetFloat("_Smoothness", own && species.smoothness > 0f ? species.smoothness : m.leaf ? .25f : .12f);
            material.SetFloat("_TintVariation", own && species.tintVariation > 0f ? species.tintVariation : m.leaf ? .18f : .06f);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Translucency", !m.leaf ? 0f : m.card ? .45f : species.translucency > 0f ? species.translucency : .8f);
            // Rocks belong to the ground: moss over their upward faces, their foot fading into it (EnvLeaf _ROCK_TOPBLEND).
            // The sink matches the depth EnvironmentFlora plants them at: stones .18, outcrops .35 of their scale.
            bool rock = species.name.StartsWith("Rock_") || species.name.StartsWith("Outcrop_") || species.name == "RockFace";
            SetToggle(material, "_RockTopBlend", "_ROCK_TOPBLEND", rock && !m.card);
            material.SetFloat("_RockSink", species.name.StartsWith("Rock_") ? .18f : .35f);
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
