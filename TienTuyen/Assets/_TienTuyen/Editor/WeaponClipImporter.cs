using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using TienTuyen.Presentation;
using UnityEditor;
using UnityEngine;

namespace TienTuyen.Editor
{
    /// <summary>
    /// Brings xdR's CS:GO third-person rifle takes (output/clip-source/xdr-csgo-anims, ValveBiped skeleton) and the
    /// CS:GO world guns (output/clip-source/gb-hl2dm-csgo-weapons) into the project, and bakes the takes onto a
    /// character profile as its weapon clip sets. Each baked clip is the take retargeted as Humanoid, plus:
    /// the ring and little fingers curled as the middle finger (the ValveBiped hand has neither), and transform curves
    /// for the profile's Weapon, WeaponRHand and WeaponLHand bones. The Weapon bone takes the source weapon_bone's
    /// place relative to the chest, so a world gun (modelled in that frame) sits on it as it sat in the source; the
    /// hand bones hold the source wrists relative to the gun, the targets the runtime solves the arms onto.
    /// Re-runnable: copies only changed source files.
    /// </summary>
    public static class WeaponClipImporter
    {
        private const string TakesSource = "output/clip-source/xdr-csgo-anims/fbx";
        private const string GunSource = "output/clip-source/gb-hl2dm-csgo-weapons/fbx/world";
        private const string AnimationDir = "Assets/_TienTuyen/Art/Animations/xdR";
        private const string GunDir = "Assets/_TienTuyen/Art/Weapons/World";
        private const string TakesPath = AnimationDir + "/xdr_ak.fbx";
        private const string RestPath = AnimationDir + "/xdr_ak@TPose.fbx";
        // Further takes on the same skeleton, optional: the per-weapon reloads.
        private const string ReloadsPath = AnimationDir + "/xdr_ak@Reloads.fbx";
        private const string BakedDir = AnimationDir + "/Baked";

        // Source bones the bake reads besides the Humanoid ones.
        private const string SourceWeapon = "ValveBiped.weapon_bone";
        // The character rig's bones the bake animates (SkinnedCharacterAnimator finds them by these names).
        private static readonly string[] TargetBones = { WeaponClipSet.WeaponBone, WeaponClipSet.RightHandBone, WeaponClipSet.LeftHandBone };
        // The same bones on a body built on the ValveBiped skeleton itself (weapon_bone under the Spine).
        private static readonly string[] ValveTargetBones = { SourceWeapon, SourceWeapon + "_RHand", SourceWeapon + "_LHand" };

        /// <summary>
        /// Game weapon id, world gun file, its reload take, and its shell-by-shell takes (start, insert, end) or none. A gun
        /// whose own takes are not imported reloads with the AK's magazine change.
        /// </summary>
        private static readonly (string weapon, string model, string reload, string[] shells)[] Guns =
        {
            ("rifle", "w_ak47", AkReload, null), ("smg", "w_mp5sd", "reload_mp5", null),
            ("shotgun", "w_xm1014", AkReload, new[] { "reload_xm_start", "reload_xm_loop", "reload_xm_end" }),
        };
        private const string AkReload = "reload";

        /// <summary>Clip set slot, xdR take, shared by every gun.</summary>
        private static readonly (string slot, string take)[] Slots =
            { ("hold", "stand_idle"), ("aim", "stand_aim"), ("run", "run_N"), ("fire", "fire") };

        private static readonly string[] Loops = { "stand_idle", "run_N", "reload_xm_loop" };

        private static readonly Dictionary<HumanBodyBones, string> ValveBiped = new Dictionary<HumanBodyBones, string>
        {
            { HumanBodyBones.Hips, "Bip01_Pelvis" },
            { HumanBodyBones.LeftUpperLeg, "Bip01_L_Thigh" }, { HumanBodyBones.LeftLowerLeg, "Bip01_L_Calf" },
            { HumanBodyBones.LeftFoot, "Bip01_L_Foot" }, { HumanBodyBones.LeftToes, "Bip01_L_Toe0" },
            { HumanBodyBones.RightUpperLeg, "Bip01_R_Thigh" }, { HumanBodyBones.RightLowerLeg, "Bip01_R_Calf" },
            { HumanBodyBones.RightFoot, "Bip01_R_Foot" }, { HumanBodyBones.RightToes, "Bip01_R_Toe0" },
            { HumanBodyBones.Spine, "Bip01_Spine" }, { HumanBodyBones.Chest, "Bip01_Spine2" }, { HumanBodyBones.UpperChest, "Bip01_Spine4" },
            { HumanBodyBones.Neck, "Bip01_Neck1" }, { HumanBodyBones.Head, "Bip01_Head1" },
            { HumanBodyBones.LeftShoulder, "Bip01_L_Clavicle" }, { HumanBodyBones.LeftUpperArm, "Bip01_L_UpperArm" },
            { HumanBodyBones.LeftLowerArm, "Bip01_L_Forearm" }, { HumanBodyBones.LeftHand, "Bip01_L_Hand" },
            { HumanBodyBones.RightShoulder, "Bip01_R_Clavicle" }, { HumanBodyBones.RightUpperArm, "Bip01_R_UpperArm" },
            { HumanBodyBones.RightLowerArm, "Bip01_R_Forearm" }, { HumanBodyBones.RightHand, "Bip01_R_Hand" },
            { HumanBodyBones.LeftThumbProximal, "Bip01_L_Finger0" }, { HumanBodyBones.LeftThumbIntermediate, "Bip01_L_Finger01" }, { HumanBodyBones.LeftThumbDistal, "Bip01_L_Finger02" },
            { HumanBodyBones.LeftIndexProximal, "Bip01_L_Finger1" }, { HumanBodyBones.LeftIndexIntermediate, "Bip01_L_Finger11" }, { HumanBodyBones.LeftIndexDistal, "Bip01_L_Finger12" },
            { HumanBodyBones.LeftMiddleProximal, "Bip01_L_Finger2" }, { HumanBodyBones.LeftMiddleIntermediate, "Bip01_L_Finger21" }, { HumanBodyBones.LeftMiddleDistal, "Bip01_L_Finger22" },
            { HumanBodyBones.RightThumbProximal, "Bip01_R_Finger0" }, { HumanBodyBones.RightThumbIntermediate, "Bip01_R_Finger01" }, { HumanBodyBones.RightThumbDistal, "Bip01_R_Finger02" },
            { HumanBodyBones.RightIndexProximal, "Bip01_R_Finger1" }, { HumanBodyBones.RightIndexIntermediate, "Bip01_R_Finger11" }, { HumanBodyBones.RightIndexDistal, "Bip01_R_Finger12" },
            { HumanBodyBones.RightMiddleProximal, "Bip01_R_Finger2" }, { HumanBodyBones.RightMiddleIntermediate, "Bip01_R_Finger21" }, { HumanBodyBones.RightMiddleDistal, "Bip01_R_Finger22" },
        }.ToDictionary(p => p.Key, p => "ValveBiped." + p.Value);

        /// <summary>A rigid pose in the model's space.</summary>
        private struct Frame
        {
            public Vector3 position;
            public Quaternion rotation;
            public Frame(Transform t, Transform root)
            {
                position = root.InverseTransformPoint(t.position);
                rotation = Quaternion.Inverse(root.rotation) * t.rotation;
            }
        }

        /// <summary>Per sampled frame of a take: the source chest, weapon_bone and both wrists.</summary>
        private sealed class Take
        {
            public float length;
            public readonly List<Frame> chest = new List<Frame>(), weapon = new List<Frame>(), right = new List<Frame>(), left = new List<Frame>();
        }

        [MenuItem("Tien Tuyen/Characters/Install CS:GO Weapon Clips (PAVN Hero)")]
        public static void InstallOnHero() => Install(AssetDatabase.LoadAssetAtPath<SkinnedCharacterProfile>(HeroCharacterImporter.ProfilePath));

        /// <summary>Imports the takes and guns and writes one clip set per gun into <paramref name="profile"/>.</summary>
        public static void Install(SkinnedCharacterProfile profile)
        {
            profile.weaponSets = BakeSets(profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            Debug.Log("CS:GO weapon clips installed on " + AssetDatabase.GetAssetPath(profile));
        }

        /// <summary>
        /// Imports the takes and guns and bakes one clip set per gun on <paramref name="profile"/>'s model (clips named after
        /// <paramref name="clipPrefix"/>, default the profile's); the profile itself is not changed.
        /// </summary>
        public static WeaponClipSet[] BakeSets(SkinnedCharacterProfile profile, string clipPrefix = null)
        {
            if (profile == null || !profile.IsValid) throw new ArgumentException("A valid Humanoid profile is required");
            CopySources();
            var rest = HumanoidRigSetup.ImportRestPose(RestPath);
            var files = TakeFiles().ToArray();
            var available = files.SelectMany(f => TakesIn(f).Select(t => (file: f, take: t))).GroupBy(t => t.take).ToDictionary(g => g.Key, g => g.First().file);
            // A gun's own takes when the files have them; otherwise the AK reload.
            string Reload(string take) => available.ContainsKey(take) ? take : AkReload;
            string[] Shells(string[] takes) => takes != null && takes.All(available.ContainsKey) ? takes : null;
            var wanted = Slots.Select(sl => sl.take).Concat(Guns.SelectMany(g => new[] { Reload(g.reload) }.Concat(Shells(g.shells) ?? Array.Empty<string>()))).Distinct().ToArray();
            foreach (string take in wanted.Where(t => !available.ContainsKey(t))) throw new InvalidOperationException("No take " + take + " in the xdR files");
            var takes = files.SelectMany(f => ReadSourceTakes(f, wanted.Where(t => available[t] == f))).ToDictionary(t => t.Key, t => t.Value);
            var avatar = ImportHumanoid(files, rest);
            var retarget = new Retarget(ReferencePose(AssetDatabase.LoadAssetAtPath<GameObject>(TakesPath), avatar), ReferencePose(profile.model, profile.avatar));
            string aimTake = Slots.First(sl => sl.slot == "aim").take;
            Vector3 aim = AimForward(profile, retarget, Clip(available[aimTake], aimTake), takes[aimTake]);
            var clips = wanted.ToDictionary(take => take, take => Bake(profile, clipPrefix ?? profile.name, retarget, aim, available[take], take, takes[take]));
            AnimationClip Slot(string slot) => clips[Slots.First(sl => sl.slot == slot).take];
            return Guns.Select(g =>
            {
                var shells = Shells(g.shells);
                return new WeaponClipSet
                {
                    weapon = g.weapon,
                    model = AssetDatabase.LoadAssetAtPath<GameObject>(GunDir + "/" + g.model + ".fbx"),
                    hold = Slot("hold"), aim = Slot("aim"), run = Slot("run"), fire = Slot("fire"), reload = clips[Reload(g.reload)],
                    reloadStart = shells != null ? clips[shells[0]] : null,
                    reloadInsert = shells != null ? clips[shells[1]] : null,
                    reloadEnd = shells != null ? clips[shells[2]] : null,
                };
            }).ToArray();
        }

        private static void CopySources()
        {
            string repo = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            Copy(Path.Combine(repo, TakesSource, "xdr_ak.fbx"), TakesPath);
            Copy(Path.Combine(repo, TakesSource, "xdr_ak@TPose.fbx"), RestPath);
            if (File.Exists(Path.Combine(repo, TakesSource, "xdr_ak@Reloads.fbx"))) Copy(Path.Combine(repo, TakesSource, "xdr_ak@Reloads.fbx"), ReloadsPath);
            foreach (string model in Guns.Select(g => g.model))
            {
                Copy(Path.Combine(repo, GunSource, model + ".fbx"), GunDir + "/" + model + ".fbx");
                // The FBX finds its textures in the .fbm folder beside it.
                foreach (string texture in Directory.GetFiles(Path.Combine(repo, GunSource, model + ".fbm"), "*.png"))
                    Copy(texture, GunDir + "/" + model + ".fbm/" + Path.GetFileName(texture));
            }
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

        /// <summary>The xdR files present: the main takes, then the optional reloads.</summary>
        private static IEnumerable<string> TakeFiles()
        {
            yield return TakesPath;
            if (AssetImporter.GetAtPath(ReloadsPath) is ModelImporter) yield return ReloadsPath;
        }

        private static IEnumerable<string> TakesIn(string path) =>
            ((ModelImporter)AssetImporter.GetAtPath(path)).importedTakeInfos.Select(t => TakeName(t.name));

        /// <param name="avatar">The main file's avatar for the others to share; null builds one from the main file.</param>
        private static ModelImporter TakesImporter(string path, ModelImporterAnimationType type, Avatar avatar = null)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.animationType = type;
            importer.avatarSetup = type != ModelImporterAnimationType.Human ? ModelImporterAvatarSetup.NoAvatar :
                avatar != null ? ModelImporterAvatarSetup.CopyFromOther : ModelImporterAvatarSetup.CreateFromThisModel;
            if (avatar != null) importer.sourceAvatar = avatar;
            importer.importAnimation = true;
            importer.resampleCurves = true;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.optimizeGameObjects = false;
            var clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                clip.name = TakeName(clip.takeName);
                // A one-frame take imports as an empty clip; span it to the next frame so its keys are kept.
                clip.lastFrame = Mathf.Max(clip.lastFrame, clip.firstFrame + 1);
                clip.loopTime = Loops.Contains(clip.name);
                // Root motion stays in the pose; only the upper body is used.
                clip.lockRootRotation = clip.lockRootHeightY = clip.lockRootPositionXZ = true;
                clip.keepOriginalOrientation = clip.keepOriginalPositionY = clip.keepOriginalPositionXZ = true;
            }
            importer.clipAnimations = clips;
            return importer;
        }

        /// <summary>FBX take "ValveBiped|stand_idle" is the take "stand_idle".</summary>
        private static string TakeName(string take) => take.Substring(take.LastIndexOf('|') + 1);

        private static AnimationClip Clip(string path, string take) =>
            AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => c.name == take)
            ?? throw new InvalidOperationException("No take " + take + " in " + path);

        /// <summary>Takes as the source skeleton plays them (a Generic import, so no retargeting), sampled per frame.</summary>
        private static Dictionary<string, Take> ReadSourceTakes(string path, IEnumerable<string> names)
        {
            TakesImporter(path, ModelImporterAnimationType.Generic).SaveAndReimport();
            var instance = (GameObject)UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            try
            {
                var root = instance.transform;
                Transform Find(string name) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name)
                    ?? throw new InvalidOperationException("No bone " + name + " in " + path);
                Transform chest = Find(ValveBiped[HumanBodyBones.UpperChest]), weapon = Find(SourceWeapon),
                    right = Find(ValveBiped[HumanBodyBones.RightHand]), left = Find(ValveBiped[HumanBodyBones.LeftHand]);
                var result = new Dictionary<string, Take>();
                foreach (string name in names)
                {
                    var clip = Clip(path, name);
                    var take = new Take { length = clip.length };
                    int frames = Mathf.Max(1, Mathf.RoundToInt(clip.length * clip.frameRate));
                    for (int f = 0; f <= frames; f++)
                    {
                        clip.SampleAnimation(instance, Mathf.Min(clip.length, f / clip.frameRate));
                        take.chest.Add(new Frame(chest, root));
                        take.weapon.Add(new Frame(weapon, root));
                        take.right.Add(new Frame(right, root));
                        take.left.Add(new Frame(left, root));
                    }
                    result[name] = take;
                }
                return result;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        /// <summary>The main file builds the ValveBiped avatar; the other files' takes import on it.</summary>
        private static Avatar ImportHumanoid(string[] files, AnimationClip restPose)
        {
            var importer = TakesImporter(TakesPath, ModelImporterAnimationType.Human);
            importer.SaveAndReimport();
            HumanoidRigSetup.Configure(importer, restPose, ValveBiped);
            var avatar = AssetDatabase.LoadAllAssetsAtPath(TakesPath).OfType<Avatar>().Single();
            if (!avatar.isValid || !avatar.isHuman) throw new InvalidOperationException("xdR Humanoid avatar is invalid");
            HumanoidRigSetup.CheckFingers(avatar, AssetDatabase.LoadAssetAtPath<GameObject>(TakesPath), ValveBiped);
            foreach (string file in files.Where(f => f != TakesPath)) TakesImporter(file, ModelImporterAnimationType.Human, avatar).SaveAndReimport();
            return avatar;
        }

        /// <summary>A model in its avatar's reference pose: world rotations of the given slots, the facing and the human scale.</summary>
        private sealed class Reference
        {
            public Quaternion hips, chest, right, left, facing;
            public float scale;
        }

        private static Reference ReferencePose(GameObject model, Avatar avatar)
        {
            var instance = (GameObject)UnityEngine.Object.Instantiate(model);
            try
            {
                var animator = instance.GetComponent<Animator>() ?? instance.AddComponent<Animator>();
                animator.avatar = avatar;
                var skeleton = avatar.humanDescription.skeleton.GroupBy(s => s.name).ToDictionary(g => g.Key, g => g.First());
                foreach (var t in instance.GetComponentsInChildren<Transform>(true))
                    if (t != instance.transform && skeleton.TryGetValue(t.name, out var bone)) { t.localPosition = bone.position; t.localRotation = bone.rotation; }
                Transform root = instance.transform;
                Quaternion World(HumanBodyBones slot) => Quaternion.Inverse(root.rotation) * animator.GetBoneTransform(slot).rotation;
                Vector3 Point(HumanBodyBones slot) => root.InverseTransformPoint(animator.GetBoneTransform(slot).position);
                Vector3 across = Point(HumanBodyBones.RightUpperArm) - Point(HumanBodyBones.LeftUpperArm);
                Vector3 up = Point(HumanBodyBones.Head) - Point(HumanBodyBones.Hips);
                return new Reference
                {
                    hips = World(HumanBodyBones.Hips), chest = World(HumanBodyBones.UpperChest), right = World(HumanBodyBones.RightHand), left = World(HumanBodyBones.LeftHand),
                    facing = Quaternion.LookRotation(Vector3.Cross(across, up), up),
                    scale = animator.humanScale,
                };
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// Source rotations onto the profile through the two reference poses (what Humanoid retargeting does to the chest and
        /// hands): target = facing * source * sourceReference^-1 * facing^-1 * targetReference. The gun is rigid, so the
        /// Weapon bone takes the source weapon_bone's rotation turned by the facing alone.
        /// </summary>
        private sealed class Retarget
        {
            public readonly Quaternion facing;
            public readonly float scale;
            private readonly Reference source, target;

            public Retarget(Reference source, Reference target)
            {
                this.source = source;
                this.target = target;
                facing = target.facing * Quaternion.Inverse(source.facing);
                scale = target.scale / source.scale;
            }

            private Quaternion Map(Quaternion rotation, Quaternion sourceReference, Quaternion targetReference) =>
                facing * rotation * Quaternion.Inverse(sourceReference) * Quaternion.Inverse(facing) * targetReference;

            public Quaternion Chest(Quaternion rotation) => Map(rotation, source.chest, target.chest);
            public Quaternion Right(Quaternion rotation) => Map(rotation, source.right, target.right);
            public Quaternion Left(Quaternion rotation) => Map(rotation, source.left, target.left);
            public Quaternion Weapon(Quaternion rotation) => facing * rotation;

            /// <summary>The profile's reference hips (model space) turned about the vertical to face <paramref name="aim"/>.</summary>
            public Quaternion HipsFacing(Vector3 aim) =>
                Quaternion.FromToRotation(Vector3.ProjectOnPlane(target.facing * Vector3.forward, Vector3.up), Vector3.ProjectOnPlane(aim, Vector3.up)) * target.hips;
        }

        /// <summary>
        /// The stance's aim in the profile's model space as the clips play it: the gun's bore (+Z) in the aim take's first
        /// frame. The clips keep the source file's orientation, which is not the profile's facing.
        /// </summary>
        private static Vector3 AimForward(SkinnedCharacterProfile profile, Retarget retarget, AnimationClip aimClip, Take aim)
        {
            var instance = (GameObject)UnityEngine.Object.Instantiate(profile.model);
            try
            {
                var animator = instance.GetComponent<Animator>() ?? instance.AddComponent<Animator>();
                animator.avatar = profile.avatar;
                aimClip.SampleAnimation(instance, 0f);
                Transform root = instance.transform, chest = animator.GetBoneTransform(HumanBodyBones.UpperChest);
                Quaternion onChest = Quaternion.Inverse(retarget.Chest(aim.chest[0].rotation)) * retarget.Weapon(aim.weapon[0].rotation);
                return Quaternion.Inverse(root.rotation) * chest.rotation * onChest * Vector3.forward;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// One take on the profile: the retargeted Humanoid clip, ring and little curled as the middle finger, the spine
        /// turned for forward hips, and the weapon bones' curves. The Weapon bone keeps the source gun's frame relative to the
        /// chest; its offset from the chest scales with the body, the wrists' offsets from the gun do not (the gun keeps its size).
        /// </summary>
        /// <param name="aim">The stance's aim in model space (<see cref="AimForward"/>).</param>
        private static AnimationClip Bake(SkinnedCharacterProfile profile, string prefix, Retarget retarget, Vector3 aim, string file, string name, Take take)
        {
            Quaternion facing = retarget.facing;

            var model = profile.model.transform;
            Transform Find(int bone) => model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == TargetBones[bone] || t.name == ValveTargetBones[bone])
                ?? throw new InvalidOperationException(profile.name + " has no " + TargetBones[bone] + " bone");
            Transform weaponBone = Find(0), rightBone = Find(1), leftBone = Find(2);
            string upperChest = profile.avatar.humanDescription.human.FirstOrDefault(h => h.humanName == HumanTrait.BoneName[(int)HumanBodyBones.UpperChest]).boneName;
            if (weaponBone.parent == null || rightBone.parent != weaponBone || leftBone.parent != rightBone)
                throw new InvalidOperationException(profile.name + ": expected " + string.Join(" > ", TargetBones) + " in one chain");
            bool onChest = weaponBone.parent.name == upperChest;

            var clip = UnityEngine.Object.Instantiate(Clip(file, name));
            clip.name = prefix + "@" + name;
            FollowMiddleFinger(clip);
            float Time(int frame) => take.weapon.Count == 1 ? 0f : take.length * frame / (take.weapon.Count - 1);
            SquareHips(profile, retarget.HipsFacing(aim), clip, take.weapon.Count, Time);
            // A weapon bone under another bone than the chest (ValveBiped: the Spine) is keyed from that bone as the
            // squared clip poses it, so the gun still sits on the chest as the source's sat on its chest.
            var parents = onChest ? null : SampleParents(profile, clip, weaponBone.parent.name, take.weapon.Count, Time);

            var curves = TargetBones.Select(_ => new AnimationCurve[7].Select(c => new AnimationCurve()).ToArray()).ToArray();
            for (int f = 0; f < take.weapon.Count; f++)
            {
                float time = Time(f);
                Frame chest = take.chest[f], weapon = take.weapon[f], right = take.right[f], left = take.left[f];
                Quaternion targetChest = retarget.Chest(chest.rotation), targetWeapon = retarget.Weapon(weapon.rotation);
                Quaternion targetRight = retarget.Right(right.rotation), targetLeft = retarget.Left(left.rotation);
                // The gun on the chest: offset in model units along the chest's axes, and rotation from the chest.
                Vector3 chestOffset = Quaternion.Inverse(targetChest) * (facing * (weapon.position - chest.position)) * retarget.scale;
                Quaternion chestTurn = Quaternion.Inverse(targetChest) * targetWeapon;
                var weaponLocal = (chestOffset / weaponBone.parent.lossyScale.x, chestTurn);
                if (!onChest)
                {
                    // Into the parent's space, chest and parent as the squared clip poses them (model space).
                    var (chestFrame, parentFrame) = parents[f];
                    Vector3 point = chestFrame.position + chestFrame.rotation * chestOffset;
                    weaponLocal = (Quaternion.Inverse(parentFrame.rotation) * (point - parentFrame.position) / weaponBone.parent.lossyScale.x,
                        Quaternion.Inverse(parentFrame.rotation) * chestFrame.rotation * chestTurn);
                }
                var locals = new[]
                {
                    weaponLocal,
                    (Quaternion.Inverse(targetWeapon) * (facing * (right.position - weapon.position)) / weaponBone.lossyScale.x,
                        Quaternion.Inverse(targetWeapon) * targetRight),
                    (Quaternion.Inverse(targetRight) * (facing * (left.position - right.position)) / rightBone.lossyScale.x,
                        Quaternion.Inverse(targetRight) * targetLeft),
                };
                for (int b = 0; b < locals.Length; b++)
                {
                    var (p, q) = locals[b];
                    // Keep consecutive keys on one hemisphere so the rotation interpolates the short way.
                    if (f > 0 && Dot(curves[b], f - 1, q) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                    float[] values = { p.x, p.y, p.z, q.x, q.y, q.z, q.w };
                    for (int i = 0; i < 7; i++) curves[b][i].AddKey(new Keyframe(time, values[i]));
                }
            }

            string[] properties = { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z", "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };
            Transform[] bones = { weaponBone, rightBone, leftBone };
            for (int b = 0; b < bones.Length; b++)
            {
                string path = AnimationUtility.CalculateTransformPath(bones[b], model);
                for (int i = 0; i < 7; i++)
                {
                    for (int k = 0; k < curves[b][i].length; k++) AnimationUtility.SetKeyLeftTangentMode(curves[b][i], k, AnimationUtility.TangentMode.Linear);
                    for (int k = 0; k < curves[b][i].length; k++) AnimationUtility.SetKeyRightTangentMode(curves[b][i], k, AnimationUtility.TangentMode.Linear);
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), properties[i]), curves[b][i]);
                }
            }

            Directory.CreateDirectory(Path.GetFullPath(Path.Combine(Application.dataPath, "..", BakedDir)));
            string clipPath = BakedDir + "/" + clip.name + ".anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (existing == null) AssetDatabase.CreateAsset(clip, clipPath);
            else
            {
                EditorUtility.CopySerialized(clip, existing);
                UnityEngine.Object.DestroyImmediate(clip);
                clip = existing;
            }
            if (!clip.isHumanMotion) throw new InvalidOperationException("Baked clip lost its Humanoid motion: " + clipPath);
            return clip;
        }

        /// <summary>
        /// The game plays these clips on the upper body over the profile's own hips, upright and facing forward, while the
        /// CS:GO stance turns and tilts its hips well away from the aim. So each frame the hips take <paramref name="square"/>
        /// (model space) and the spine takes the difference (its muscles are rewritten), which leaves the chest, and the gun
        /// on it, where the source had them.
        /// </summary>
        private static void SquareHips(SkinnedCharacterProfile profile, Quaternion square, AnimationClip clip, int frames, Func<int, float> time)
        {
            var instance = (GameObject)UnityEngine.Object.Instantiate(profile.model);
            var handler = default(HumanPoseHandler);
            try
            {
                var animator = instance.GetComponent<Animator>() ?? instance.AddComponent<Animator>();
                animator.avatar = profile.avatar;
                Transform root = instance.transform, hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                Transform chest = animator.GetBoneTransform(HumanBodyBones.UpperChest);
                var spine = new[] { HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest }
                    .Select(animator.GetBoneTransform).Where(t => t != null).ToArray();
                var muscles = Enumerable.Range(0, HumanTrait.MuscleCount)
                    .Where(m => spine.Contains(animator.GetBoneTransform((HumanBodyBones)HumanTrait.BoneFromMuscle(m)))).ToArray();
                var curves = muscles.Select(_ => new AnimationCurve()).ToArray();
                handler = new HumanPoseHandler(profile.avatar, root);
                var pose = new HumanPose();
                for (int f = 0; f < frames; f++)
                {
                    clip.SampleAnimation(instance, time(f));
                    Quaternion kept = chest.rotation;
                    hips.rotation = root.rotation * square;
                    // Each spine bone takes its share of what is left, the last all of it.
                    for (int i = 0; i < spine.Length; i++)
                        spine[i].rotation = Quaternion.Slerp(Quaternion.identity, kept * Quaternion.Inverse(chest.rotation), 1f / (spine.Length - i)) * spine[i].rotation;
                    handler.GetHumanPose(ref pose);
                    for (int m = 0; m < muscles.Length; m++) curves[m].AddKey(new Keyframe(time(f), pose.muscles[muscles[m]]));
                }
                for (int m = 0; m < muscles.Length; m++)
                {
                    for (int k = 0; k < curves[m].length; k++) AnimationUtility.SetKeyLeftTangentMode(curves[m], k, AnimationUtility.TangentMode.Linear);
                    for (int k = 0; k < curves[m].length; k++) AnimationUtility.SetKeyRightTangentMode(curves[m], k, AnimationUtility.TangentMode.Linear);
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), HumanTrait.MuscleName[muscles[m]]), curves[m]);
                }
            }
            finally
            {
                handler?.Dispose();
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        /// <summary>Per frame of <paramref name="clip"/> on the profile's model: its UpperChest and bone <paramref name="parent"/>, model space.</summary>
        private static List<(Frame chest, Frame parent)> SampleParents(SkinnedCharacterProfile profile, AnimationClip clip, string parent, int frames, Func<int, float> time)
        {
            var instance = (GameObject)UnityEngine.Object.Instantiate(profile.model);
            try
            {
                var animator = instance.GetComponent<Animator>() ?? instance.AddComponent<Animator>();
                animator.avatar = profile.avatar;
                Transform root = instance.transform, chest = animator.GetBoneTransform(HumanBodyBones.UpperChest);
                Transform bone = root.GetComponentsInChildren<Transform>(true).First(t => t.name == parent);
                var result = new List<(Frame, Frame)>(frames);
                for (int f = 0; f < frames; f++)
                {
                    clip.SampleAnimation(instance, time(f));
                    result.Add((new Frame(chest, root), new Frame(bone, root)));
                }
                return result;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static float Dot(AnimationCurve[] curve, int key, Quaternion q) =>
            curve[3][key].value * q.x + curve[4][key].value * q.y + curve[5][key].value * q.z + curve[6][key].value * q.w;

        /// <summary>Ring and little fingers bend with the middle finger (muscle space is relative to each avatar's straight reference).</summary>
        private static void FollowMiddleFinger(AnimationClip clip)
        {
            var bindings = AnimationUtility.GetCurveBindings(clip);
            foreach (string side in new[] { "LeftHand", "RightHand" })
                for (int joint = 1; joint <= 3; joint++)
                {
                    string middle = side + ".Middle." + joint + " Stretched";
                    var from = bindings.FirstOrDefault(b => b.propertyName == middle);
                    if (from.propertyName == null) throw new InvalidOperationException(clip.name + " has no " + middle + " curve");
                    var curve = AnimationUtility.GetEditorCurve(clip, from);
                    foreach (string finger in new[] { "Ring", "Little" })
                        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(from.path, from.type, side + "." + finger + "." + joint + " Stretched"), curve);
                }
        }
    }
}
