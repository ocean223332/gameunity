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
    /// Installs the duel's slot-1 US soldier on the ValveBiped skeleton (output/us-slot1: the Tripo body skinned through
    /// Auto-Rig Pro, five fingers per hand, A-pose bind, scale 1) as US_ValveProfile, from PAVN_HeroProfile (left as it is).
    /// It moves and carries the CS:GO guns like the hero (<see cref="HeroValveImporter"/>) on clips baked for its own body;
    /// until it has a hand correction of its own its grips come from the hero's, its fingers carried onto its own bind.
    /// Batch: Unity -batchmode -projectPath TienTuyen -executeMethod TienTuyen.Editor.USSoldierValveImporter.Import -quit
    /// </summary>
    public static class USSoldierValveImporter
    {
        public const string ArtDir = "Assets/_TienTuyen/Art/Characters/ValveBiped/USSoldier";
        public const string ResourceDir = "Assets/_TienTuyen/Art/Resources/Characters/US";
        public const string ProfilePath = ResourceDir + "/US_ValveProfile.asset";
        private const string Type = "USSoldier";
        private const string Source = "output/us-slot1";
        private const string RestPath = "Assets/_TienTuyen/Art/Animations/xdR/xdr_ak@TPose.fbx";
        // Its own baked clips, so neither the hero's nor the enemies' import changes them.
        private const string ClipPrefix = "USSoldierValve";
        private static readonly (string weapon, string gun)[] Guns = { ("rifle", "ak47"), ("smg", "mp5sd"), ("shotgun", "xm1014") };

        [MenuItem("Tien Tuyen/Characters/Import US Soldier (ValveBiped)")]
        public static void Import()
        {
            CopySources();
            var source = AssetDatabase.LoadAssetAtPath<SkinnedCharacterProfile>(HeroCharacterImporter.ProfilePath)
                ?? throw new InvalidOperationException("Missing " + HeroCharacterImporter.ProfilePath);
            var (model, avatar) = ValveBipedCharacterImporter.ImportBody(Type, HumanoidRigSetup.ImportRestPose(RestPath), ArtDir);
            Directory.CreateDirectory(ResourceDir);
            var profile = AssetDatabase.LoadAssetAtPath<SkinnedCharacterProfile>(ProfilePath);
            if (profile == null) { profile = ScriptableObject.CreateInstance<SkinnedCharacterProfile>(); AssetDatabase.CreateAsset(profile, ProfilePath); }
            EditorUtility.CopySerialized(source, profile);
            profile.name = Path.GetFileNameWithoutExtension(ProfilePath);
            profile.model = model;
            profile.avatar = avatar;
            profile.modelScale = 1f;
            // Its own hands on the viewmodels (USSoldierArms_<viewmodel>.json, ViewmodelArmsPostprocessor).
            profile.firstPersonArms = "USSoldier";
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
            string json = Path.Combine(Repo, HeroValveImporter.Correction);
            // The correction's finger rotations are the hero's, local to its finger bind frames.
            var hero = AssetDatabase.LoadAssetAtPath<GameObject>(HeroValveImporter.ArtDir + "/HeroValve.fbx")
                ?? throw new InvalidOperationException("Missing " + HeroValveImporter.ArtDir + "/HeroValve.fbx");
            profile.weaponSets = Guns.Select(g =>
            {
                var set = sets.First(s => s.weapon == g.weapon);
                var takes = new[] { set.fire, set.reload, set.reloadStart, set.reloadInsert, set.reloadEnd }.Where(c => c != null)
                    .ToDictionary(c => c.name.Substring(c.name.IndexOf('@') + 1), c => c);
                set.grip = HandGripImporter.Import(json, g.gun, ArtDir + "/USSoldierGrip_" + g.gun + ".asset", takes, model, hero);
                return set;
            }).ToArray();
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            if (!profile.IsValid) throw new InvalidOperationException("US soldier ValveBiped profile is invalid: " + ProfilePath);
            Debug.Log("US soldier ValveBiped profile imported: " + ProfilePath + ", speed scale " + k.ToString("0.000"));
        }

        private static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));

        /// <summary>output/us-slot1's body and textures as USSoldierValve.* in <see cref="ArtDir"/>.</summary>
        private static void CopySources()
        {
            foreach (var (from, to) in new[] { ("US_Valve.fbx", ".fbx"), ("US_Valve_BaseColor.png", "_BaseColor.png"), ("US_Valve_Normal.png", "_Normal.png") })
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
