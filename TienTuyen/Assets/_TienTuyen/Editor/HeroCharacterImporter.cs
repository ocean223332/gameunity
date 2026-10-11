using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using TienTuyen.Presentation;
using UnityEditor;
using UnityEngine;

namespace TienTuyen.Editor
{
    /// <summary>
    /// Brings the PAVN hero (rig, textures, clips from output/characters/pavn-hero) into the
    /// project and builds its runtime profile. Re-runnable: copies only changed source files.
    /// The hero clips the enemies use are theirs (EnemyCharacterImporter); none of this reaches them.
    /// Batch: Unity -batchmode -projectPath TienTuyen -executeMethod TienTuyen.Editor.HeroCharacterImporter.Import -quit
    /// </summary>
    public static class HeroCharacterImporter
    {
        public const string Art = "Assets/_TienTuyen/Art/Characters/PAVN";
        public const string ResourceDir = "Assets/_TienTuyen/Art/Resources/Characters/PAVN";
        public const string ProfilePath = ResourceDir + "/PAVN_HeroProfile.asset";
        public const string RigPath = Art + "/PAVN_Hero_Rig.fbx";
        public const string PrefabPath = Art + "/PAVN_Hero.prefab";
        public const string MaterialPath = Art + "/PAVN_Hero.mat";
        public const string MaskPath = Art + "/PAVN_UpperBody.mask";
        // Rig with 15 finger segments, tips, forearm twist and weapon bones (output/hand-rig), its clips
        // re-baked on it, and the straight-finger take that sets the avatar's finger reference pose.
        private const string SourceRig = "output/characters/pavn-hero/hand-rig-v1";
        private const string SourceTextures = "output/characters/pavn-hero/v2/textures";
        private const string RestPose = "TPose";

        // Planted-foot speed of Run_Forward at 1x, measured in Blender (animation-v2 build).
        private const float RunForwardSpeed = 4.33f;

        // The handmade strafes and backward run hold three cycles so the loop seam keeps its
        // neighbours; the middle one (frames 12-24 after the take's first frame, 0.4 s, left
        // touchdown first) is the loop, authored for 4.75 m/s.
        private const string HandmadeStrafeLeft = "Handmade_Strafe_Left_v2", HandmadeStrafeRight = "Handmade_Strafe_Right_v1", HandmadeRunBackward = "Handmade_Run_Backward_v1";
        private const int HandmadeLocomotionFirstFrame = 12, HandmadeLocomotionLastFrame = 24;
        private const float HandmadeLocomotionSpeed = 4.75f;

        private static readonly string[] Loops = { "Idle_Rifle", "Aim_Hold", "Run_Forward", HandmadeStrafeLeft, HandmadeStrafeRight, HandmadeRunBackward };
        private static readonly string[] Clips = { "Idle_Rifle", "Aim_Hold", "Run_Forward", "Reload", "Hit_Reaction", "Death_Backward", HandmadeStrafeLeft, HandmadeStrafeRight, HandmadeRunBackward, RestPose };

        [MenuItem("Tien Tuyen/Characters/Import PAVN Hero")]
        public static void Import()
        {
            CopySources();
            var avatar = ImportRig();
            var material = BuildMaterial();
            var mask = BuildMask();
            var model = BuildPrefab(avatar, material);
            var profile = AssetDatabase.LoadAssetAtPath<SkinnedCharacterProfile>(ProfilePath);
            if (profile == null)
            {
                Directory.CreateDirectory(ResourceDir);
                profile = ScriptableObject.CreateInstance<SkinnedCharacterProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            profile.model = model;
            profile.avatar = avatar;
            profile.upperBodyMask = mask;
            profile.idle = ImportClip("Idle_Rifle", avatar);
            profile.aim = ImportClip("Aim_Hold", avatar);
            profile.runForward = ImportClip("Run_Forward", avatar);
            profile.runBackward = ImportClip(HandmadeRunBackward, avatar, HandmadeLocomotionFirstFrame, HandmadeLocomotionLastFrame);
            profile.strafeLeft = ImportClip(HandmadeStrafeLeft, avatar, HandmadeLocomotionFirstFrame, HandmadeLocomotionLastFrame);
            profile.strafeRight = ImportClip(HandmadeStrafeRight, avatar, HandmadeLocomotionFirstFrame, HandmadeLocomotionLastFrame);
            profile.reload = ImportClip("Reload", avatar);
            profile.hit = ImportClip("Hit_Reaction", avatar);
            profile.death = ImportClip("Death_Backward", avatar);
            profile.runForwardSpeed = RunForwardSpeed;
            profile.runBackwardSpeed = profile.strafeLeftSpeed = profile.strafeRightSpeed = HandmadeLocomotionSpeed;
            profile.maxPlaybackRate = 1.3f;
            profile.modelScale = 1f;
            profile.deathFloorLift = SkinnedCharacterProfileBaker.MeasureDeathFloorLift(profile);
            SkinnedCharacterProfileBaker.MeasureSoles(profile);
            SkinnedCharacterProfileBaker.MeasureBack(profile);
            profile.bannerSource = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_TienTuyen/Art/Resources/Models/hero.fbx");
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            if (!profile.IsValid) throw new InvalidOperationException("PAVN hero profile is not valid: " + ProfilePath);
            WeaponClipImporter.Install(profile);
            Debug.Log("PAVN hero profile imported: " + ProfilePath);
        }

        private static void CopySources()
        {
            string repo = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            string rig = Path.Combine(repo, SourceRig), textures = Path.Combine(repo, SourceTextures);
            if (!Directory.Exists(rig)) throw new DirectoryNotFoundException(rig);
            Copy(Path.Combine(rig, "PAVN_Hero_Rig.fbx"), RigPath);
            foreach (string clip in Clips)
                Copy(Path.Combine(rig, "PAVN_Hero@" + clip + ".fbx"), ClipPath(clip));
            foreach (string map in new[] { "BaseColor", "Normal", "MetallicSmoothness" })
                Copy(Path.Combine(textures, "PAVN_Hero_" + map + ".png"), TexturePath(map));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static void Copy(string source, string assetPath)
        {
            if (!File.Exists(source)) throw new FileNotFoundException(source);
            string target = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
            if (File.Exists(target) && Hash(target) == Hash(source)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(source, target, true);
        }

        private static string Hash(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return Convert.ToBase64String(sha.ComputeHash(stream));
        }

        private static string ClipPath(string clip) => Art + "/Animations/PAVN_Hero@" + clip + ".fbx";
        private static string TexturePath(string map) => Art + "/Textures/PAVN_Hero_" + map + ".png";

        private static Avatar ImportRig()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(RigPath);
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false;
            importer.optimizeGameObjects = false;
            importer.skinWeights = ModelImporterSkinWeights.Standard;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importBlendShapes = false;
            importer.importCameras = importer.importLights = false;
            importer.SaveAndReimport();
            HumanoidRigSetup.Configure(importer, HumanoidRigSetup.ImportRestPose(ClipPath(RestPose)));
            var avatar = AssetDatabase.LoadAllAssetsAtPath(RigPath).OfType<Avatar>().Single();
            if (!avatar.isValid || !avatar.isHuman) throw new InvalidOperationException("PAVN hero Humanoid avatar is invalid");
            HumanoidRigSetup.CheckFingers(avatar, AssetDatabase.LoadAssetAtPath<GameObject>(RigPath));
            return avatar;
        }

        private static AnimationClip ImportClip(string name, Avatar avatar, int firstFrame = -1, int lastFrame = -1)
        {
            string path = ClipPath(name);
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            importer.sourceAvatar = avatar;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.resampleCurves = true;
            // Each clip file has one root node; keep it so curve paths match the rig.
            importer.preserveHierarchy = true;
            var clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                clip.name = name;
                clip.loopTime = Loops.Contains(name);
                if (firstFrame >= 0) { float takeStart = clip.firstFrame; clip.firstFrame = takeStart + firstFrame; clip.lastFrame = takeStart + lastFrame; }
                // Root motion stays in the pose: the gameplay actor alone moves the character.
                clip.lockRootRotation = clip.lockRootHeightY = clip.lockRootPositionXZ = true;
                clip.keepOriginalOrientation = clip.keepOriginalPositionY = clip.keepOriginalPositionXZ = true;
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            var result = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));
            if (result == null || !result.isHumanMotion) throw new InvalidOperationException("Not a Humanoid clip: " + path);
            return result;
        }

        private static Material BuildMaterial()
        {
            var baseMap = Texture(TexturePath("BaseColor"), TextureImporterType.Default, true);
            var normal = Texture(TexturePath("Normal"), TextureImporterType.NormalMap, false);
            var metallic = Texture(TexturePath("MetallicSmoothness"), TextureImporterType.Default, false);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP Lit shader is unavailable");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, MaterialPath); }
            material.shader = shader;
            material.SetTexture("_BaseMap", baseMap);
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_BumpScale", 1f);
            material.EnableKeyword("_NORMALMAP");
            // R = metallic, A = smoothness (URP Lit metallic workflow).
            material.SetTexture("_MetallicGlossMap", metallic);
            material.SetFloat("_Metallic", 1f);
            material.SetFloat("_Smoothness", 1f);
            material.SetFloat("_SmoothnessTextureChannel", 0f);
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Texture2D Texture(string path, TextureImporterType type, bool srgb)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = type;
            importer.sRGBTexture = srgb;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static AvatarMask BuildMask()
        {
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
            if (mask == null) { mask = new AvatarMask(); AssetDatabase.CreateAsset(mask, MaskPath); }
            // Aim, reload and hit own the spine, head and arms; legs and root stay on locomotion.
            for (var part = AvatarMaskBodyPart.Root; part < AvatarMaskBodyPart.LastBodyPart; part++)
                mask.SetHumanoidBodyPartActive(part,
                    part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.Head ||
                    part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm ||
                    part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers ||
                    part == AvatarMaskBodyPart.LeftHandIK || part == AvatarMaskBodyPart.RightHandIK);
            EditorUtility.SetDirty(mask);
            return mask;
        }

        private static GameObject BuildPrefab(Avatar avatar, Material material)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RigPath));
            try
            {
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                instance.name = "PAVN_Hero";
                var animator = instance.GetComponent<Animator>() ?? instance.AddComponent<Animator>();
                animator.avatar = avatar;
                animator.runtimeAnimatorController = null;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                foreach (var skin in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    skin.sharedMaterial = material;
                    skin.quality = SkinQuality.Bone4;
                    // Fixed bounds that also cover the lying death pose (about 0.7 m behind the feet).
                    skin.updateWhenOffscreen = false;
                    var root = skin.rootBone != null ? skin.rootBone : skin.transform;
                    var center = root.InverseTransformPoint(instance.transform.position + new Vector3(0f, .85f, -.3f));
                    skin.localBounds = new Bounds(center, root.InverseTransformVector(new Vector3(2.2f, 2.2f, 2.6f)).Abs());
                }
                return PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static Vector3 Abs(this Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
    }
}
