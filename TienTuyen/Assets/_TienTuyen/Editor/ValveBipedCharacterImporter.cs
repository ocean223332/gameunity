#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using TienTuyen.Presentation;

namespace TienTuyen.Editor
{
    /// <summary>
    /// Installs the four enemy bodies fitted on the exact xdR ValveBiped bind (output/enemy/work, Hunyuan bodies with
    /// MakeHuman gloved hands) as the enemy profiles. Each keeps the actions and timings of the US rifleman profile it
    /// replaces. Infantry and Charger play the CS:GO knife takes as they are (they share the xdR bind), with the US
    /// infantry flinch on an upper-body mask that leaves the fingers to the takes. Shooter and Elite keep the US clips
    /// with speeds rescaled to the new body, and carry the CS:GO MP5-SD and AK on clips baked for the ValveBiped
    /// skeleton, with each body's own hand correction (output/enemy/work/corr) when it exists.
    /// </summary>
    public static class ValveBipedCharacterImporter
    {
        public const string ArtDir = "Assets/_TienTuyen/Art/Characters/ValveBiped/Enemies";
        public const string ResourceDir = "Assets/_TienTuyen/Art/Resources/Characters/Enemies";
        private const string RestPath = "Assets/_TienTuyen/Art/Animations/xdR/xdr_ak@TPose.fbx";
        private const string OldDir = EnemyCharacterImporter.ResourceDir;
        private const string CorrectionDir = "output/enemy/work/corr";
        // Prefix of the enemies' baked clips (baked on the first gun body, Shooter, and played by Elite too); the hero bakes its own.
        private const string ClipPrefix = "ValveBiped";
        /// <summary>Uniform scale of the Elite body (gun and grips scale with it); the others stay at the bind's own size.</summary>
        public const float EliteScale = 1.1f;
        // Fist centre from the wrist (metres along the hand, toward the palm) on the MakeHuman hands, set by render.
        private static readonly Vector2 DaggerFist = new Vector2(.075f, .025f);
        // Planted-foot speeds of the knife runs on the xdR skeleton (m/s) and their cycle (s), measured by
        // output/clip-source/_scripts/xdr_knife.py. E is the right strafe (Source +Y is left).
        private const float KnifeRunN = 3.9f, KnifeRunS = 3.21f, KnifeRunE = 2.47f, KnifeRunW = 2.33f, KnifeRunCycle = .843f;

        private static readonly (string type, string source, string weapon, string gun)[] Types =
        {
            ("Infantry", "USRiflemanInfantryProfile", null, null),
            ("Charger", "USRiflemanChargerProfile", null, null),
            ("Shooter", "USRiflemanProfile", "smg", "mp5sd"),
            ("Elite", "USRiflemanProfile", "rifle", "ak47"),
        };

        public static string ProfilePath(string type) => ResourceDir + "/" + type + "ValveProfile.asset";

        internal static readonly Dictionary<HumanBodyBones, string> Bones = new Dictionary<HumanBodyBones, string>
        {
            { HumanBodyBones.Hips, "ValveBiped.Bip01_Pelvis" },
            { HumanBodyBones.LeftUpperLeg, "ValveBiped.Bip01_L_Thigh" }, { HumanBodyBones.LeftLowerLeg, "ValveBiped.Bip01_L_Calf" },
            { HumanBodyBones.LeftFoot, "ValveBiped.Bip01_L_Foot" }, { HumanBodyBones.LeftToes, "ValveBiped.Bip01_L_Toe0" },
            { HumanBodyBones.RightUpperLeg, "ValveBiped.Bip01_R_Thigh" }, { HumanBodyBones.RightLowerLeg, "ValveBiped.Bip01_R_Calf" },
            { HumanBodyBones.RightFoot, "ValveBiped.Bip01_R_Foot" }, { HumanBodyBones.RightToes, "ValveBiped.Bip01_R_Toe0" },
            { HumanBodyBones.Spine, "ValveBiped.Bip01_Spine" }, { HumanBodyBones.Chest, "ValveBiped.Bip01_Spine2" }, { HumanBodyBones.UpperChest, "ValveBiped.Bip01_Spine4" },
            { HumanBodyBones.Neck, "ValveBiped.Bip01_Neck1" }, { HumanBodyBones.Head, "ValveBiped.Bip01_Head1" },
            { HumanBodyBones.LeftShoulder, "ValveBiped.Bip01_L_Clavicle" }, { HumanBodyBones.LeftUpperArm, "ValveBiped.Bip01_L_UpperArm" },
            { HumanBodyBones.LeftLowerArm, "ValveBiped.Bip01_L_Forearm" }, { HumanBodyBones.LeftHand, "ValveBiped.Bip01_L_Hand" },
            { HumanBodyBones.RightShoulder, "ValveBiped.Bip01_R_Clavicle" }, { HumanBodyBones.RightUpperArm, "ValveBiped.Bip01_R_UpperArm" },
            { HumanBodyBones.RightLowerArm, "ValveBiped.Bip01_R_Forearm" }, { HumanBodyBones.RightHand, "ValveBiped.Bip01_R_Hand" },
            { HumanBodyBones.LeftThumbProximal, "ValveBiped.Bip01_L_Finger0" }, { HumanBodyBones.LeftThumbIntermediate, "ValveBiped.Bip01_L_Finger01" }, { HumanBodyBones.LeftThumbDistal, "ValveBiped.Bip01_L_Finger02" },
            { HumanBodyBones.LeftIndexProximal, "ValveBiped.Bip01_L_Finger1" }, { HumanBodyBones.LeftIndexIntermediate, "ValveBiped.Bip01_L_Finger11" }, { HumanBodyBones.LeftIndexDistal, "ValveBiped.Bip01_L_Finger12" },
            { HumanBodyBones.LeftMiddleProximal, "ValveBiped.Bip01_L_Finger2" }, { HumanBodyBones.LeftMiddleIntermediate, "ValveBiped.Bip01_L_Finger21" }, { HumanBodyBones.LeftMiddleDistal, "ValveBiped.Bip01_L_Finger22" },
            { HumanBodyBones.RightThumbProximal, "ValveBiped.Bip01_R_Finger0" }, { HumanBodyBones.RightThumbIntermediate, "ValveBiped.Bip01_R_Finger01" }, { HumanBodyBones.RightThumbDistal, "ValveBiped.Bip01_R_Finger02" },
            { HumanBodyBones.RightIndexProximal, "ValveBiped.Bip01_R_Finger1" }, { HumanBodyBones.RightIndexIntermediate, "ValveBiped.Bip01_R_Finger11" }, { HumanBodyBones.RightIndexDistal, "ValveBiped.Bip01_R_Finger12" },
            { HumanBodyBones.RightMiddleProximal, "ValveBiped.Bip01_R_Finger2" }, { HumanBodyBones.RightMiddleIntermediate, "ValveBiped.Bip01_R_Finger21" }, { HumanBodyBones.RightMiddleDistal, "ValveBiped.Bip01_R_Finger22" },
        };

        [MenuItem("Tien Tuyen/Characters/Import ValveBiped Enemies")]
        public static void ImportEnemies()
        {
            var rest = HumanoidRigSetup.ImportRestPose(RestPath);
            var infantry = Load(OldDir + "/USRiflemanInfantryProfile.asset");
            var knife = WeaponClipImporter.ImportKnifeTakes();
            var knifeMask = KnifeUpperBodyMask();
            WeaponClipSet[] sets = null;
            Directory.CreateDirectory(ResourceDir);
            foreach (var (type, sourceName, weapon, gun) in Types)
            {
                var source = Load(OldDir + "/" + sourceName + ".asset");
                // The knife bodies play the knife takes as they are, so they take the takes' reference pose.
                var (model, avatar) = ImportBody(type, rest, null, type == "Infantry" || type == "Charger" ? (knife.model, knife.avatar) : default);
                string path = ProfilePath(type);
                var profile = AssetDatabase.LoadAssetAtPath<SkinnedCharacterProfile>(path);
                if (profile == null) { profile = ScriptableObject.CreateInstance<SkinnedCharacterProfile>(); AssetDatabase.CreateAsset(profile, path); }
                EditorUtility.CopySerialized(source, profile);
                profile.name = Path.GetFileNameWithoutExtension(path);
                float scale = type == "Elite" ? EliteScale : 1f;
                // Planted-foot speeds and dash cycle scale with the human size of the body that plays the clips.
                float k = HumanScale(model, avatar) / HumanScale(source.model, source.avatar) * scale / Mathf.Max(.01f, source.modelScale);
                profile.runForwardSpeed *= k; profile.runBackwardSpeed *= k;
                profile.strafeLeftSpeed *= k; profile.strafeRightSpeed *= k;
                profile.chargeDashCycleDistance *= k;
                profile.model = model;
                profile.avatar = avatar;
                profile.modelScale = scale;
                profile.upperBodyMask = null;
                profile.weaponSets = new WeaponClipSet[0];
                if (type == "Charger")
                {
                    // The dagger rides the hand on the infantry guard between dashes.
                    profile.aim = infantry.aim;
                    profile.hit = infantry.hit;
                    profile.reload = null;
                    profile.guardHold = true;
                }
                if (profile.guardHold)
                    Knife(profile, knife.clips, infantry.hit, knifeMask,
                        HumanScale(model, avatar) / HumanScale(knife.model, knife.avatar) * scale);
                // The dagger's handle sits in the closed MakeHuman fist.
                if (profile.guardHold) profile.fistChannel = DaggerFist;
                profile.deathFloorLift = SkinnedCharacterProfileBaker.MeasureDeathFloorLift(profile);
                SkinnedCharacterProfileBaker.MeasureSoles(profile);
                SkinnedCharacterProfileBaker.MeasureBack(profile);
                if (weapon != null)
                {
                    sets = sets ?? WeaponClipImporter.BakeSets(profile, ClipPrefix);
                    var set = Copy(sets.First(s => s.weapon == weapon));
                    set.grip = Grip(type, gun, set);
                    profile.weaponSets = new[] { set };
                }
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                if (!profile.IsValid) throw new InvalidOperationException(type + " ValveBiped profile is invalid");
                Debug.Log("ValveBiped " + type + " profile imported: " + path + (weapon != null ? " grip " + (profile.weaponSets[0].grip != null) : ""));
            }
        }

        /// <summary>
        /// A knife body on the CS:GO knife takes. Every take carries its own knife arms, so there is no separate guard layer;
        /// the dagger attack keeps its contact time and the dash runs on the forward knife run.
        /// </summary>
        /// <param name="k">Human size of the body over the xdR skeleton's.</param>
        private static void Knife(SkinnedCharacterProfile profile, Dictionary<string, AnimationClip> takes, AnimationClip hit, AvatarMask mask, float k)
        {
            AnimationClip Take(string name) => takes.TryGetValue(name, out var clip) ? clip : throw new InvalidOperationException("No knife take " + name);
            profile.idle = Take("stand_idle");
            profile.aim = null;
            profile.runForward = Take("run_N"); profile.runBackward = Take("run_S");
            profile.strafeLeft = Take("run_W"); profile.strafeRight = Take("run_E");
            profile.runForwardSpeed = KnifeRunN * k; profile.runBackwardSpeed = KnifeRunS * k;
            profile.strafeLeftSpeed = KnifeRunW * k; profile.strafeRightSpeed = KnifeRunE * k;
            profile.runCarriesUpperBody = false;
            profile.reload = null;
            profile.hit = hit;
            profile.death = Take("death");
            profile.upperBodyMask = mask;
            if (profile.meleeAttack != null) profile.meleeAttack = Take("attack");
            if (profile.chargeDash != null)
            {
                profile.chargeTelegraph = Take("stand_aim");
                profile.chargeDash = Take("run_N");
                profile.chargeDashCycleDistance = KnifeRunN * KnifeRunCycle * k;
                profile.chargeRecovery = Take("stand_idle");
            }
        }

        /// <summary>Spine, head and arms without the fingers: the flinch and the attack leave the hands to the knife takes.</summary>
        private static AvatarMask KnifeUpperBodyMask()
        {
            string path = ArtDir + "/KnifeUpperBody.mask";
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
            if (mask == null) { mask = new AvatarMask(); AssetDatabase.CreateAsset(mask, path); }
            for (var part = AvatarMaskBodyPart.Root; part < AvatarMaskBodyPart.LastBodyPart; part++)
                mask.SetHumanoidBodyPartActive(part,
                    part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.Head ||
                    part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm ||
                    part == AvatarMaskBodyPart.LeftHandIK || part == AvatarMaskBodyPart.RightHandIK);
            EditorUtility.SetDirty(mask);
            return mask;
        }

        private static SkinnedCharacterProfile Load(string path) =>
            AssetDatabase.LoadAssetAtPath<SkinnedCharacterProfile>(path) ?? throw new InvalidOperationException("Missing " + path);

        /// <summary>The body FBX as a Humanoid on the exact bind, its material (and a sleeve submesh's), and a prefab with them assigned
        /// (<paramref name="dir"/>/&lt;type&gt;Valve.*, default the enemy folder).</summary>
        /// <param name="reference">An avatar on the same skeleton whose takes the body plays unbaked; the body then takes its
        /// reference pose (see <see cref="MatchReference"/>).</param>
        internal static (GameObject, Avatar) ImportBody(string type, AnimationClip rest, string dir = null, (GameObject model, Avatar avatar) reference = default)
        {
            dir = dir ?? ArtDir + "/" + type; string fbx = dir + "/" + type + "Valve.fbx";
            var importer = (ModelImporter)AssetImporter.GetAtPath(fbx) ?? throw new FileNotFoundException(fbx);
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false;
            importer.isReadable = true;
            importer.optimizeGameObjects = false;
            // A body with a sleeve (<type>Valve_Sleeve_* textures) keeps its source materials' names to find the sleeve's submesh.
            bool sleeved = File.Exists(Path.Combine(Path.GetDirectoryName(Application.dataPath), dir + "/" + type + "Valve_Sleeve_BaseColor.png"));
            importer.materialImportMode = sleeved ? ModelImporterMaterialImportMode.ImportViaMaterialDescription : ModelImporterMaterialImportMode.None;
            importer.skinWeights = ModelImporterSkinWeights.Custom;
            importer.maxBonesPerVertex = 4;
            importer.SaveAndReimport();
            HumanoidRigSetup.Configure(importer, rest, Bones);
            if (reference.avatar != null) MatchReference(importer, AssetDatabase.LoadAssetAtPath<GameObject>(fbx), reference);
            var avatar = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Avatar>().Single();
            if (!avatar.isValid || !avatar.isHuman) throw new InvalidOperationException(type + " ValveBiped avatar is invalid");
            HumanoidRigSetup.CheckFingers(avatar, AssetDatabase.LoadAssetAtPath<GameObject>(fbx), Bones);

            var material = AssetDatabase.LoadAssetAtPath<Material>(dir + "/" + type + "Valve.mat");
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? throw new InvalidOperationException("URP Lit shader is unavailable");
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, dir + "/" + type + "Valve.mat"); }
            material.shader = shader;
            material.SetTexture("_BaseMap", Texture(dir + "/" + type + "Valve_BaseColor.png", false));
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BumpMap", Texture(dir + "/" + type + "Valve_Normal.png", true));
            material.SetFloat("_BumpScale", 1f);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", .22f);
            material.EnableKeyword("_NORMALMAP");
            EditorUtility.SetDirty(material);
            // The sleeve's submesh (the source material named *Sleeve*) takes its own material.
            Material sleeve = null;
            if (sleeved)
            {
                string path = dir + "/" + type + "Valve_Sleeve.mat";
                sleeve = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (sleeve == null) { sleeve = new Material(shader); AssetDatabase.CreateAsset(sleeve, path); }
                sleeve.CopyPropertiesFromMaterial(material);
                sleeve.shader = shader;
                sleeve.SetTexture("_BaseMap", Texture(dir + "/" + type + "Valve_Sleeve_BaseColor.png", false));
                sleeve.SetTexture("_BumpMap", Texture(dir + "/" + type + "Valve_Sleeve_Normal.png", true));
                sleeve.EnableKeyword("_NORMALMAP");
                EditorUtility.SetDirty(sleeve);
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbx));
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            instance.name = type + "Valve";
            var animator = instance.GetComponent<Animator>() ?? instance.AddComponent<Animator>();
            animator.avatar = avatar;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (sleeve == null) renderer.sharedMaterial = material;
                else
                {
                    var source = renderer.sharedMaterials;
                    int at = Array.FindIndex(source, m => m != null && m.name.Contains("Sleeve"));
                    if (at < 0 || source.Length != renderer.sharedMesh.subMeshCount)
                        throw new InvalidOperationException(type + " body has sleeve textures but no *Sleeve* source material among " + source.Length);
                    renderer.sharedMaterials = source.Select((m, i) => i == at ? sleeve : material).ToArray();
                }
                renderer.quality = SkinQuality.Bone4;
                renderer.updateWhenOffscreen = true;
            }
            var prefab = PrefabUtility.SaveAsPrefabAsset(instance, dir + "/" + type + "Valve.prefab");
            UnityEngine.Object.DestroyImmediate(instance);
            return (prefab, avatar);
        }

        /// <summary>
        /// Sets the body's reference pose to <paramref name="reference"/>'s: each ValveBiped bone keeps that avatar's
        /// rotation in model space. Unity generates each file's T from its own bind and leaves the hands rolled and the
        /// hips turned differently per file, and Humanoid retargets relative to the two references; with one reference
        /// a take plays on the body exactly as on its own skeleton.
        /// </summary>
        private static void MatchReference(ModelImporter importer, GameObject model, (GameObject model, Avatar avatar) reference)
        {
            var wanted = ReferenceRotations(reference.model, reference.avatar.humanDescription.skeleton);
            var description = importer.humanDescription;
            var skeleton = description.skeleton;
            var index = Enumerable.Range(0, skeleton.Length).GroupBy(i => skeleton[i].name).ToDictionary(g => g.Key, g => g.First());
            var current = ReferenceRotations(model, skeleton);
            var instance = UnityEngine.Object.Instantiate(model);
            try
            {
                // Parents first, so each bone's local rotation is taken against its parent's matched one.
                foreach (var t in instance.GetComponentsInChildren<Transform>(true))
                {
                    if (t == instance.transform || !t.name.StartsWith("ValveBiped.Bip01") || !wanted.TryGetValue(t.name, out var rotation) || !index.TryGetValue(t.name, out int i)) continue;
                    skeleton[i].rotation = Quaternion.Inverse(current[t.parent.name]) * rotation;
                    current[t.name] = rotation;
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
            description.skeleton = skeleton;
            importer.humanDescription = description;
            importer.SaveAndReimport();
        }

        /// <summary>Each bone's rotation in the model's space (its root included) with the skeleton's local rotations, by name.</summary>
        private static Dictionary<string, Quaternion> ReferenceRotations(GameObject model, SkeletonBone[] skeleton)
        {
            var instance = UnityEngine.Object.Instantiate(model);
            try
            {
                var local = skeleton.GroupBy(s => s.name).ToDictionary(g => g.Key, g => g.First().rotation);
                instance.transform.SetPositionAndRotation(Vector3.zero, local.TryGetValue(instance.name, out var root) ? root : Quaternion.identity);
                var result = new Dictionary<string, Quaternion>();
                foreach (var t in instance.GetComponentsInChildren<Transform>(true))
                {
                    if (t != instance.transform && local.TryGetValue(t.name, out var rotation)) t.localRotation = rotation;
                    result[t.name] = t.rotation;
                }
                return result;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static Texture2D Texture(string path, bool normal)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path) ?? throw new FileNotFoundException(path);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !normal;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
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

        private static WeaponClipSet Copy(WeaponClipSet s) => new WeaponClipSet
        {
            weapon = s.weapon, model = s.model, hold = s.hold, aim = s.aim, run = s.run, fire = s.fire, reload = s.reload,
            reloadStart = s.reloadStart, reloadInsert = s.reloadInsert, reloadEnd = s.reloadEnd,
        };

        /// <summary>The body's hand correction for the gun (output/enemy/work/corr/&lt;type&gt;_&lt;gun&gt;.json), or null before it is solved.</summary>
        private static HandGripData Grip(string type, string gun, WeaponClipSet set)
        {
            string repo = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            string json = Path.Combine(repo, CorrectionDir, type.ToLowerInvariant() + "_" + gun + ".json");
            if (!File.Exists(json)) { Debug.LogWarning("No hand correction yet: " + json); return null; }
            var takes = new[] { set.reload, set.reloadStart, set.reloadInsert, set.reloadEnd }.Where(c => c != null)
                .ToDictionary(c => c.name.Substring(c.name.IndexOf('@') + 1), c => c);
            return HandGripImporter.Import(json, gun, ArtDir + "/" + type + "/" + type + "Grip_" + gun + ".asset", takes);
        }
    }
}
#endif
