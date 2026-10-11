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
    /// Installs the PAVN hero fitted on the exact xdR ValveBiped bind (output/hero-valve: Hunyuan body, MakeHuman hands,
    /// scale 1) as the player profile PAVN_ValveProfile. It keeps the locomotion, hit and death clips of PAVN_HeroProfile
    /// (left as it is) with speeds rescaled to the new body, and carries all three CS:GO guns on the ValveBiped clips the
    /// enemies share, each with the hero's hand correction (output/hero-valve/hand_correction.json).
    /// Batch: Unity -batchmode -projectPath TienTuyen -executeMethod TienTuyen.Editor.HeroValveImporter.Import -quit
    /// </summary>
    public static class HeroValveImporter
    {
        public const string ArtDir = "Assets/_TienTuyen/Art/Characters/ValveBiped/Hero";
        public const string ProfilePath = HeroCharacterImporter.ResourceDir + "/PAVN_ValveProfile.asset";
        private const string Type = "Hero";
        private const string Source = "output/hero-valve";
        private const string RestPath = "Assets/_TienTuyen/Art/Animations/xdR/xdr_ak@TPose.fbx";
        private const string ClipPrefix = "ValveBiped";
        private static readonly (string weapon, string gun)[] Guns = { ("rifle", "ak47"), ("smg", "mp5sd"), ("shotgun", "xm1014") };

        [MenuItem("Tien Tuyen/Characters/Import PAVN Hero (ValveBiped)")]
        public static void Import()
        {
            CopySources();
            var source = AssetDatabase.LoadAssetAtPath<SkinnedCharacterProfile>(HeroCharacterImporter.ProfilePath)
                ?? throw new InvalidOperationException("Missing " + HeroCharacterImporter.ProfilePath);
            var (model, avatar) = ValveBipedCharacterImporter.ImportBody(Type, HumanoidRigSetup.ImportRestPose(RestPath), ArtDir);
            var profile = AssetDatabase.LoadAssetAtPath<SkinnedCharacterProfile>(ProfilePath);
            if (profile == null) { profile = ScriptableObject.CreateInstance<SkinnedCharacterProfile>(); AssetDatabase.CreateAsset(profile, ProfilePath); }
            EditorUtility.CopySerialized(source, profile);
            profile.name = Path.GetFileNameWithoutExtension(ProfilePath);
            // Planted-foot speeds scale with the human size of the body that plays the clips.
            float k = HumanScale(model, avatar) / HumanScale(source.model, source.avatar) / Mathf.Max(.01f, source.modelScale);
            profile.runForwardSpeed *= k; profile.runBackwardSpeed *= k;
            profile.strafeLeftSpeed *= k; profile.strafeRightSpeed *= k;
            profile.model = model;
            profile.avatar = avatar;
            profile.modelScale = 1f;
            profile.upperBodyMask = null;
            profile.weaponSets = new WeaponClipSet[0];
            profile.deathFloorLift = SkinnedCharacterProfileBaker.MeasureDeathFloorLift(profile);
            SkinnedCharacterProfileBaker.MeasureSoles(profile);
            SkinnedCharacterProfileBaker.MeasureBack(profile);
            var sets = WeaponClipImporter.BakeSets(profile, ClipPrefix);
            string json = Path.Combine(Repo, Source, "hand_correction.json");
            profile.weaponSets = Guns.Select(g =>
            {
                var set = sets.First(s => s.weapon == g.weapon);
                var takes = new[] { set.reload, set.reloadStart, set.reloadInsert, set.reloadEnd }.Where(c => c != null)
                    .ToDictionary(c => c.name.Substring(c.name.IndexOf('@') + 1), c => c);
                set.grip = HandGripImporter.Import(json, g.gun, ArtDir + "/HeroGrip_" + g.gun + ".asset", takes);
                return set;
            }).ToArray();
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            if (!profile.IsValid) throw new InvalidOperationException("PAVN ValveBiped profile is invalid: " + ProfilePath);
            Debug.Log("PAVN ValveBiped hero profile imported: " + ProfilePath + ", speed scale " + k.ToString("0.000"));
        }

        private static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));

        /// <summary>output/hero-valve's body and textures as HeroValve.* in <see cref="ArtDir"/>.</summary>
        private static void CopySources()
        {
            foreach (var (from, to) in new[] { ("PAVN_Valve.fbx", ".fbx"), ("PAVN_Valve_BaseColor.png", "_BaseColor.png"), ("PAVN_Valve_Normal.png", "_Normal.png") })
            {
                string src = Path.Combine(Repo, Source, from), dst = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ArtDir + "/" + Type + "Valve" + to));
                if (!File.Exists(src)) throw new FileNotFoundException(src);
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                if (!File.Exists(dst) || !File.ReadAllBytes(dst).SequenceEqual(File.ReadAllBytes(src))) File.Copy(src, dst, true);
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
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
