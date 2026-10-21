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
            // The knife: the CS:GO knife takes' slash, which meets its target on frame 8 of 30, and their idle.
            profile.meleeAttack = knife.clips["attack"];
            profile.meleeContactTime = 8f / 30f;
            profile.knifeIdle = knife.clips["stand_idle"];
            var sets = WeaponClipImporter.BakeSets(profile, ClipPrefix, crouch: true, walk: true);
            // Planted-foot speeds scale with the human size of the body that plays the takes.
            float k = HumanScale(model, avatar) / HumanScale(knife.model, knife.avatar);
            ValveBipedCharacterImporter.GunLocomotion(profile, k, knife.clips["death"]);
            ValveBipedCharacterImporter.CrouchLocomotion(profile, k);
            ValveBipedCharacterImporter.WalkLocomotion(profile, k);
            profile.deathFloorLift = SkinnedCharacterProfileBaker.MeasureDeathFloorLift(profile);
            SkinnedCharacterProfileBaker.MeasureSoles(profile);
            profile.weaponSets = Guns.Select(g =>
            {
                var set = sets.First(s => s.weapon == g.weapon);
                // The committed grip is the hero's hands, hand edits included; only a missing one is solved from the correction.
                set.grip = AssetDatabase.LoadAssetAtPath<HandGripData>(GripPath(g.gun)) ?? SolveGrip(set, g.gun, model);
                return set;
            }).ToArray();
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            if (!profile.IsValid) throw new InvalidOperationException("PAVN ValveBiped profile is invalid: " + ProfilePath);
            Debug.Log("PAVN ValveBiped hero profile imported: " + ProfilePath + ", speed scale " + k.ToString("0.000"));
        }

        private static string GripPath(string gun) => ArtDir + "/HeroGrip_" + gun + ".asset";

        private static HandGripData SolveGrip(WeaponClipSet set, string gun, GameObject model)
        {
            var takes = new[] { set.fire, set.reload, set.reloadStart, set.reloadInsert, set.reloadEnd }.Where(c => c != null)
                .ToDictionary(c => c.name.Substring(c.name.IndexOf('@') + 1), c => c);
            return HandGripImporter.Import(Path.Combine(Repo, Correction), gun, GripPath(gun), takes, model);
        }

        /// <summary>
        /// Re-solves the hero's three grips from <see cref="Correction"/> over the committed ones: the step after editing the
        /// correction (the Blender grip loop), never part of <see cref="Import"/>.
        /// </summary>
        [MenuItem("Tien Tuyen/Characters/Re-solve PAVN Hero Grips")]
        public static void ResolveGrips()
        {
            var profile = AssetDatabase.LoadAssetAtPath<SkinnedCharacterProfile>(ProfilePath)
                ?? throw new InvalidOperationException("Missing " + ProfilePath);
            var before = profile.weaponSets.Where(s => s.grip != null).ToDictionary(s => s.grip, s => EditorJsonUtility.ToJson(s.grip, true));
            foreach (var set in profile.weaponSets)
                set.grip = SolveGrip(set, Guns.First(g => g.weapon == set.weapon).gun, profile.model);
            // The committed grips can hold hand edits the correction lacks: name what changed, and keep the committed grips
            // unless the change is confirmed (in batch, with -overwriteGrips).
            var changed = before.Select(b => (grip: b.Key, lines: Changed(b.Value, EditorJsonUtility.ToJson(b.Key, true))))
                .Where(c => c.lines.Length > 0).ToArray();
            if (changed.Length > 0)
            {
                string report = string.Join("\n", changed.Select(c => c.grip.name + ": " + c.lines.Length + " lines differ, first:\n  " +
                    string.Join("\n  ", c.lines.Take(6))));
                bool overwrite = Application.isBatchMode ? Environment.GetCommandLineArgs().Contains("-overwriteGrips")
                    : EditorUtility.DisplayDialog("Re-solve hero grips", "The correction differs from the committed grips:\n" + report, "Overwrite", "Keep committed");
                if (!overwrite)
                {
                    foreach (var c in changed)
                    {
                        EditorJsonUtility.FromJsonOverwrite(before[c.grip], c.grip);
                        EditorUtility.SetDirty(c.grip);
                    }
                    AssetDatabase.SaveAssets();
                    Debug.LogWarning("Hero grips kept; the correction would change:\n" + report);
                    return;
                }
                Debug.LogWarning("Hero grips overwritten from the correction:\n" + report);
            }
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
        }

        /// <summary>The lines of <paramref name="after"/> that differ from <paramref name="before"/> at the same index.</summary>
        private static string[] Changed(string before, string after)
        {
            string[] a = before.Split('\n'), b = after.Split('\n');
            return Enumerable.Range(0, Math.Max(a.Length, b.Length))
                .Where(i => i >= a.Length || i >= b.Length || a[i] != b[i]).Select(i => i < b.Length ? b[i].Trim() : "(removed)").ToArray();
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
        /// The body's material (ImportBody's) made matte: the base colour at 2048 (4K looked the same at the closest film camera), metallic 0 and the smoothness the
        /// MetalSmooth map's alpha holds (output/hero-valve: the helmet about .15, cloth and skin about .1).
        /// </summary>
        private static void Matte(GameObject model)
        {
            var material = model.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterial;
            var color = (TextureImporter)AssetImporter.GetAtPath(ArtDir + "/" + Type + "Valve_BaseColor.png");
            if (color.maxTextureSize != 2048) { color.maxTextureSize = 2048; color.SaveAndReimport(); }
            string mapPath = ArtDir + "/" + Type + "Valve_MetalSmooth.png";
            var map = (TextureImporter)AssetImporter.GetAtPath(mapPath);
            // Two smoothness values over flat areas: compressed at 1024 it reads the same at a sixteenth of the memory.
            if (map.sRGBTexture || map.textureCompression != TextureImporterCompression.Compressed || map.maxTextureSize != 1024)
            {
                map.sRGBTexture = false;
                map.textureCompression = TextureImporterCompression.Compressed;
                map.maxTextureSize = 1024;
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
