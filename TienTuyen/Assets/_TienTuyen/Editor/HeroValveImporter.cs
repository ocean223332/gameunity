#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using TienTuyen.Presentation;
using UnityEditor;
using UnityEngine;

namespace TienTuyen.Editor
{
    /// <summary>
    /// Installs the PAVN hero on the ValveBiped skeleton (output/hero-valve: the Tripo body with its own finger rest, ring and
    /// little fingers included, scale 1) as the player profile PAVN_ValveProfile, from PAVN_HeroProfile (left as it is). It
    /// moves on the CS:GO AK takes (<see cref="ValveBipedCharacterImporter.GunLocomotion"/>) and carries all three CS:GO guns
    /// on clips baked for its own body, each with the hero's hand correction (<see cref="Correction"/>).
    /// Batch: Unity -batchmode -projectPath TienTuyen -executeMethod TienTuyen.Editor.HeroValveImporter.Import -quit
    /// </summary>
    public static class HeroValveImporter
    {
        public const string ArtDir = "Assets/_TienTuyen/Art/Characters/ValveBiped/Hero";
        public const string ProfilePath = HeroCharacterImporter.ResourceDir + "/PAVN_ValveProfile.asset";
        private const string Type = "Hero";
        private const string Source = "output/hero-valve";
        private const string RestPath = "Assets/_TienTuyen/Art/Animations/xdR/xdr_ak@TPose.fbx";
        // The hero's own baked clips; the enemies bake theirs as ValveBiped@, so neither import changes the other's.
        private const string ClipPrefix = "HeroValve";
        /// <summary>The hero's hand correction, repo-relative: grips, per-frame reload hands, per-gun fire fingers, the XM shell.</summary>
        public const string Correction = "output/hero-valve/work/hy_b/grip_D/hand_correction_live.json";
        private static readonly (string weapon, string gun)[] Guns = { ("rifle", "ak47"), ("smg", "mp5sd"), ("shotgun", "xm1014") };

        [MenuItem("Tien Tuyen/Characters/Import PAVN Hero (ValveBiped)")]
        public static void Import()
        {
            CopySources();
            var source = AssetDatabase.LoadAssetAtPath<SkinnedCharacterProfile>(HeroCharacterImporter.ProfilePath)
                ?? throw new InvalidOperationException("Missing " + HeroCharacterImporter.ProfilePath);
            var (model, avatar) = ValveBipedCharacterImporter.ImportBody(Type, HumanoidRigSetup.ImportRestPose(RestPath), ArtDir);
            Matte(model);
            var profile = AssetDatabase.LoadAssetAtPath<SkinnedCharacterProfile>(ProfilePath);
            if (profile == null) { profile = ScriptableObject.CreateInstance<SkinnedCharacterProfile>(); AssetDatabase.CreateAsset(profile, ProfilePath); }
            EditorUtility.CopySerialized(source, profile);
            profile.name = Path.GetFileNameWithoutExtension(ProfilePath);
            profile.model = model;
            profile.avatar = avatar;
            profile.modelScale = 1f;
            profile.upperBodyMask = null;
            profile.weaponSets = new WeaponClipSet[0];
            var knife = WeaponClipImporter.ImportKnifeTakes();
            var sets = WeaponClipImporter.BakeSets(profile, ClipPrefix, crouch: true, walk: true);
            // Planted-foot speeds scale with the human size of the body that plays the takes.
            float k = HumanScale(model, avatar) / HumanScale(knife.model, knife.avatar);
            ValveBipedCharacterImporter.GunLocomotion(profile, k, knife.clips["death"]);
            ValveBipedCharacterImporter.CrouchLocomotion(profile, k);
            ValveBipedCharacterImporter.WalkLocomotion(profile, k);
            profile.deathFloorLift = SkinnedCharacterProfileBaker.MeasureDeathFloorLift(profile);
            SkinnedCharacterProfileBaker.MeasureSoles(profile);
            SkinnedCharacterProfileBaker.MeasureBack(profile);
            string json = Path.Combine(Repo, Correction);
            profile.weaponSets = Guns.Select(g =>
            {
                var set = sets.First(s => s.weapon == g.weapon);
                var takes = new[] { set.fire, set.reload, set.reloadStart, set.reloadInsert, set.reloadEnd }.Where(c => c != null)
                    .ToDictionary(c => c.name.Substring(c.name.IndexOf('@') + 1), c => c);
                set.grip = HandGripImporter.Import(json, g.gun, ArtDir + "/HeroGrip_" + g.gun + ".asset", takes, model);
                return set;
            }).ToArray();
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            if (!profile.IsValid) throw new InvalidOperationException("PAVN ValveBiped profile is invalid: " + ProfilePath);
            Debug.Log("PAVN ValveBiped hero profile imported: " + ProfilePath + ", speed scale " + k.ToString("0.000"));
        }

        private static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));

        /// <summary>output/hero-valve's body and textures (and the sleeve's, when it has one) as HeroValve.* in <see cref="ArtDir"/>.</summary>
        private static void CopySources()
        {
            foreach (var (from, to, optional) in new[] { ("PAVN_Valve.fbx", ".fbx", false), ("PAVN_Valve_BaseColor.png", "_BaseColor.png", false),
                         ("PAVN_Valve_Normal.png", "_Normal.png", false), ("PAVN_Valve_MetalSmooth.png", "_MetalSmooth.png", false), ("PAVN_Sleeve_BaseColor.png", "_Sleeve_BaseColor.png", true), ("PAVN_Sleeve_Normal.png", "_Sleeve_Normal.png", true) })
            {
                string src = Path.Combine(Repo, Source, from), dst = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ArtDir + "/" + Type + "Valve" + to));
                if (!File.Exists(src) && optional) continue;
                if (!File.Exists(src)) throw new FileNotFoundException(src);
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                if (!File.Exists(dst) || !File.ReadAllBytes(dst).SequenceEqual(File.ReadAllBytes(src))) File.Copy(src, dst, true);
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        /// <summary>
        /// The body's material (ImportBody's) made matte: the base colour at its full 4K, metallic 0 and the smoothness the
        /// MetalSmooth map's alpha holds (output/hero-valve: the helmet about .15, cloth and skin about .1).
        /// </summary>
        private static void Matte(GameObject model)
        {
            var material = model.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterial;
            var color = (TextureImporter)AssetImporter.GetAtPath(ArtDir + "/" + Type + "Valve_BaseColor.png");
            if (color.maxTextureSize != 4096) { color.maxTextureSize = 4096; color.SaveAndReimport(); }
            string mapPath = ArtDir + "/" + Type + "Valve_MetalSmooth.png";
            var map = (TextureImporter)AssetImporter.GetAtPath(mapPath);
            if (map.sRGBTexture || map.textureCompression != TextureImporterCompression.Uncompressed)
            {
                map.sRGBTexture = false;
                map.textureCompression = TextureImporterCompression.Uncompressed;
                map.SaveAndReimport();
            }
            material.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(mapPath));
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.SetFloat("_SmoothnessTextureChannel", 0f);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 1f);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
        }

        private static float HumanScale(GameObject model, Avatar avatar)
        {
            var instance = (GameObject)UnityEngine.Object.Instantiate(model);
            try
            {
                var animator = instance.GetComponent<Animator>() ?? instance.AddComponent<Animator>();
                animator.avatar = avatar;
                return animator.humanScale;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }
    }
}
#endif
