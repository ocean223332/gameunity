#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using TienTuyen.Presentation;

namespace TienTuyen.Editor
{
    public static class EnemyCharacterImporter
    {
        public const string Art = "Assets/_TienTuyen/Art/Characters/USRifleman";
        public const string ResourceDir = "Assets/_TienTuyen/Art/Characters/USRifleman/Profiles"; // outside Resources: only the ValveBiped profiles ship
        public const string RigPath = Art + "/US_Rifleman_Rig.fbx";
        public const string MaterialPath = Art + "/USRifleman.mat";
        const string TextureId = "tripo_image_7bd6e247-8f75-47f9-9bd0-e665de2d4647";
        const string DataId = "20261008024947_1207fcd1_fbx_Clone1";

        [MenuItem("Tien Tuyen/Characters/Import US Rifleman")]
        public static void Import()
        {
            var avatar = ImportModelAndMaterial();
            Directory.CreateDirectory(ResourceDir);
            AssetDatabase.Refresh();
            var instance = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(RigPath));
            instance.name = "USRifleman";
            var animator = instance.GetComponent<Animator>();
            if (animator == null) animator = instance.AddComponent<Animator>();
            animator.avatar = avatar;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
                renderer.quality = SkinQuality.Bone4;
                renderer.updateWhenOffscreen = true;
            }
            var model = PrefabUtility.SaveAsPrefabAsset(instance, ResourceDir + "/USRifleman.prefab");
            UnityEngine.Object.DestroyImmediate(instance);
            var path = ResourceDir + "/USRiflemanProfile.asset";
            var profile = AssetDatabase.LoadAssetAtPath<SkinnedCharacterProfile>(path);
            if (profile == null) { profile = ScriptableObject.CreateInstance<SkinnedCharacterProfile>(); AssetDatabase.CreateAsset(profile, path); }
            profile.model = model;
            profile.avatar = avatar;
            profile.idle = HeroClip("Idle_Rifle");
            profile.aim = HeroClip("Aim_Hold");
            profile.runForward = Clip(Art + "/Animations/US_Rifleman@Run_Forward.fbx");
            profile.runBackward = HeroClip("Run_Backward");
            profile.strafeLeft = HeroClip("Strafe_Left");
            profile.strafeRight = HeroClip("Strafe_Right");
            profile.reload = HeroClip("Reload");
            profile.hit = HeroClip("Hit_Reaction");
            profile.death = HeroClip("Death_Backward");
            profile.runForwardSpeed = 4.70f;
            profile.runBackwardSpeed = 2.26f;
            profile.strafeLeftSpeed = 1.90f;
            profile.strafeRightSpeed = 2.58f;
            profile.maxPlaybackRate = 1.3f;
            profile.modelScale = 1;
            profile.deathFloorLift = SkinnedCharacterProfileBaker.MeasureDeathFloorLift(profile);
            SkinnedCharacterProfileBaker.MeasureSoles(profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            if (!profile.IsValid) throw new InvalidOperationException("US rifleman profile is invalid");
            Debug.Log("US rifleman profile imported: " + path);
        }

        public const string ActionSource = "../output/characters/us-rifleman/enemy-actions-v1";

        /// <summary>
        /// Infantry, Charger and Elite profiles: clones of the shooter profile plus the enemy action
        /// clips and a cloth tint per variant. The melee-only Infantry and Elite never shoulder their
        /// weapon: each has its own guard and hit (the Elite with its E-tool). Run after <see cref="Import"/>; the shooter profile itself is left untouched.
        /// </summary>
        [MenuItem("Tien Tuyen/Characters/Import US Rifleman Enemy Actions")]
        public static void ImportActions()
        {
            var shooter = AssetDatabase.LoadAssetAtPath<SkinnedCharacterProfile>(ResourceDir + "/USRiflemanProfile.asset");
            if (shooter == null || !shooter.IsValid) throw new InvalidOperationException("Import the US rifleman first");
            var avatar = AssetDatabase.LoadAllAssetsAtPath(RigPath).OfType<Avatar>().Single();
            string[] names = { "Infantry_Guard", "Infantry_Melee", "Infantry_Hit", "Charger_Telegraph", "Charger_Dash", "Charger_Recovery", "Elite_Guard", "Elite_Chop", "Elite_Hit", "Elite_Run" };
            foreach (var name in names)
            {
                string source = Path.Combine(ActionSource, "US_Rifleman@" + name + ".fbx");
                if (!File.Exists(source)) throw new FileNotFoundException(source);
                File.Copy(source, ActionClipPath(name), true);
            }
            AssetDatabase.Refresh();
            foreach (var name in names)
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(ActionClipPath(name));
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar = avatar;
                importer.animationCompression = ModelImporterAnimationCompression.Off;
                importer.resampleCurves = true;
                importer.preserveHierarchy = true;
                importer.importCameras = importer.importLights = false;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                var clips = importer.defaultClipAnimations;
                foreach (var clip in clips)
                {
                    clip.loopTime = name == "Charger_Dash" || name == "Elite_Run";
                    clip.lockRootRotation = clip.lockRootHeightY = clip.lockRootPositionXZ = true;
                    clip.keepOriginalOrientation = clip.keepOriginalPositionY = clip.keepOriginalPositionXZ = true;
                }
                importer.clipAnimations = clips;
                importer.SaveAndReimport();
            }

            var infantry = VariantProfile(shooter, "Infantry", TintedModel(shooter.model, "Infantry", InfantryTint), 1f);
            infantry.aim = Clip(ActionClipPath("Infantry_Guard"));
            infantry.hit = Clip(ActionClipPath("Infantry_Hit"));
            infantry.reload = null;
            infantry.meleeAttack = Clip(ActionClipPath("Infantry_Melee"));
            infantry.meleeContactTime = 8f / 30f;
            infantry.guardHold = true;
            EditorUtility.SetDirty(infantry);

            var charger = VariantProfile(shooter, "Charger", TintedModel(shooter.model, "Charger", ChargerTint), 1f);
            charger.chargeTelegraph = Clip(ActionClipPath("Charger_Telegraph"));
            charger.chargeDash = Clip(ActionClipPath("Charger_Dash"));
            charger.chargeDashCycleDistance = 4.6667f;
            charger.chargeRecovery = Clip(ActionClipPath("Charger_Recovery"));
            EditorUtility.SetDirty(charger);

            var elite = VariantProfile(shooter, "Elite", TintedModel(shooter.model, "Elite", EliteTint), 1.1f);
            elite.aim = Clip(ActionClipPath("Elite_Guard"));
            elite.hit = Clip(ActionClipPath("Elite_Hit"));
            elite.reload = null;
            elite.meleeAttack = Clip(ActionClipPath("Elite_Chop"));
            elite.meleeContactTime = 12f / 30f;
            elite.guardHold = true;
            // Charging run with the E-tool on the shoulder: authored at 3.0 / 1.1 m/s in rig metres, 3.0 m/s drawn at 1.1.
            elite.runForward = Clip(ActionClipPath("Elite_Run"));
            elite.runForwardSpeed = 3f / 1.1f * elite.modelScale;
            elite.runCarriesUpperBody = true;
            // Fist channel measured on the closed E-tool grip (elite-run-v1 grip_lib.FIST).
            elite.fistChannel = new Vector2(.035f, .028f);
            EditorUtility.SetDirty(elite);
            AssetDatabase.SaveAssets();
            Debug.Log("US rifleman enemy action profiles imported");
        }

        static string ActionClipPath(string name) => Art + "/Animations/US_Rifleman@" + name + ".fbx";

        static SkinnedCharacterProfile VariantProfile(SkinnedCharacterProfile shooter, string variant, GameObject model, float scale)
        {
            string path = ResourceDir + "/USRifleman" + variant + "Profile.asset";
            var profile = AssetDatabase.LoadAssetAtPath<SkinnedCharacterProfile>(path);
            if (profile == null) { profile = ScriptableObject.CreateInstance<SkinnedCharacterProfile>(); AssetDatabase.CreateAsset(profile, path); }
            EditorUtility.CopySerialized(shooter, profile);
            profile.name = Path.GetFileNameWithoutExtension(path);
            profile.model = model;
            profile.modelScale = scale;
            // Clip speeds are planted-foot speeds of the unscaled rig; a scaled body covers proportionally more.
            profile.runForwardSpeed *= scale; profile.runBackwardSpeed *= scale;
            profile.strafeLeftSpeed *= scale; profile.strafeRightSpeed *= scale;
            return profile;
        }

        // Base colour multipliers of the rifleman texture. Each variant is a separate brightness step
        // (Elite < Shooter 1 < Infantry < Charger) so the types still separate in grayscale under the
        // dim, warm game light; the Charger is the old procedural khaki.
        static readonly Color EliteTint = new Color(.7f, .7f, .7f, 1f);
        static readonly Color InfantryTint = new Color(1.36f, 1.27f, 1.10f, 1f);
        static readonly Color ChargerTint = new Color(1.55f, 1.40f, 1.12f, 1f);

        /// <summary>The rifleman prefab with a tinted copy of its material (the shooter's stays untouched).</summary>
        static GameObject TintedModel(GameObject model, string variant, Color tint)
        {
            string materialPath = Art + "/USRifleman" + variant + ".mat";
            var source = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null) { material = new Material(source); AssetDatabase.CreateAsset(material, materialPath); }
            else material.CopyPropertiesFromMaterial(source);
            material.SetColor("_BaseColor", tint);
            EditorUtility.SetDirty(material);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = "USRifleman" + variant;
            foreach (var renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>()) renderer.sharedMaterial = material;
            var tinted = PrefabUtility.SaveAsPrefabAsset(instance, ResourceDir + "/USRifleman" + variant + ".prefab");
            UnityEngine.Object.DestroyImmediate(instance);
            return tinted;
        }

        // The hero clips the riflemen use, kept apart from the hero's own: they import against
        // HeroShared/PAVN_Hero_Rig_Frozen.fbx, a copy of the hero rig as it was, so hero rig changes do not reach them.
        static AnimationClip HeroClip(string name) => Clip(Art + "/Animations/HeroShared/PAVN_Hero@" + name + ".fbx");

        static AnimationClip Clip(string path)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (clip == null || !clip.isHumanMotion) throw new InvalidOperationException("Import the hero/enemy Humanoid clips first: " + path);
            return clip;
        }

        public static Avatar ImportModelAndMaterial()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(RigPath);
            if (importer == null) throw new FileNotFoundException(RigPath);
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false;
            importer.isReadable = true;
            importer.optimizeGameObjects = false;
            importer.skinWeights = ModelImporterSkinWeights.Custom;
            importer.maxBonesPerVertex = 4;
            importer.minBoneWeight = 0;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.SaveAndReimport();
            var avatar = AssetDatabase.LoadAllAssetsAtPath(RigPath).OfType<Avatar>().Single();
            if (!avatar.isValid || !avatar.isHuman) throw new InvalidOperationException("US rifleman Humanoid Avatar is invalid");

            var baseMap = Texture(Art + "/Textures/" + TextureId + "_0_0.jpg", true, false);
            var normal = Texture(Art + "/Textures/" + TextureId + "_0_3.jpg", false, true);
            var roughness = Texture(Art + "/Textures/" + DataId + "_roughness.JPEG", false, false, true);
            var metallic = Texture(Art + "/Textures/" + DataId + "_metallic.JPEG", false, false, true);
            if (roughness.width != metallic.width || roughness.height != metallic.height)
                throw new InvalidOperationException("Metallic and roughness map dimensions differ");
            var r = roughness.GetPixels32();
            var m = metallic.GetPixels32();
            var packed = new Color32[r.Length];
            for (int i = 0; i < packed.Length; i++) packed[i] = new Color32(m[i].r, 0, 0, (byte)(255 - r[i].r));
            var texture = new Texture2D(roughness.width, roughness.height, TextureFormat.RGBA32, true, true);
            texture.SetPixels32(packed);
            texture.Apply();
            string packedPath = Art + "/Textures/MetallicSmoothness.png";
            File.WriteAllBytes(packedPath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(packedPath, ImportAssetOptions.ForceSynchronousImport);
            var metalSmooth = Texture(packedPath, false, false);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP Lit shader is unavailable");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, MaterialPath); }
            material.shader = shader;
            material.SetTexture("_BaseMap", baseMap);
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_BumpScale", 1);
            material.SetTexture("_MetallicGlossMap", metalSmooth);
            material.SetFloat("_Metallic", 1);
            material.SetFloat("_Smoothness", 1);
            material.SetFloat("_SmoothnessTextureChannel", 0);
            material.EnableKeyword("_NORMALMAP");
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            EditorUtility.SetDirty(material);

            string runPath = Art + "/Animations/US_Rifleman@Run_Forward.fbx";
            var run = (ModelImporter)AssetImporter.GetAtPath(runPath);
            run.animationType = ModelImporterAnimationType.Human;
            run.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            run.sourceAvatar = avatar;
            run.animationCompression = ModelImporterAnimationCompression.Off;
            run.resampleCurves = true;
            run.preserveHierarchy = true;
            run.materialImportMode = ModelImporterMaterialImportMode.None;
            var clips = run.defaultClipAnimations;
            foreach (var clip in clips)
            {
                clip.loopTime = true;
                clip.lockRootRotation = clip.lockRootHeightY = clip.lockRootPositionXZ = true;
                clip.keepOriginalOrientation = clip.keepOriginalPositionY = clip.keepOriginalPositionXZ = true;
            }
            run.clipAnimations = clips;
            run.SaveAndReimport();
            AssetDatabase.SaveAssets();
            return avatar;
        }

        static Texture2D Texture(string path, bool srgb, bool normal, bool readable = false)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null) throw new FileNotFoundException(path);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = srgb;
            importer.isReadable = readable;
            importer.maxTextureSize = 1024;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
#endif
