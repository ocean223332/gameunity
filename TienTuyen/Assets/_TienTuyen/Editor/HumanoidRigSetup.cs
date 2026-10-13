using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TienTuyen.Editor
{
    /// <summary>
    /// Humanoid avatar setup for a character rig from a map of HumanBodyBones slot to bone name; without a map, from
    /// bones named after their slot. Only mapped bones are humanoid (finger tips, twist and weapon bones stay out),
    /// and the fingers' reference pose comes from a one-frame straight-finger take instead of the modelled curl,
    /// so a finger curl retargets from any avatar whose reference fingers are straight too.
    /// </summary>
    public static class HumanoidRigSetup
    {
        private static readonly string[] Fingers = { "Thumb", "Index", "Middle", "Ring", "Little" };
        private static readonly string[] Segments = { "Proximal", "Intermediate", "Distal" };
        private const float StraightTolerance = 5f;

        /// <summary>Imports a one-frame take as Generic so its pose can be read.</summary>
        public static AnimationClip ImportRestPose(string path)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            importer.importAnimation = true;
            importer.resampleCurves = true;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.preserveHierarchy = true;
            // A one-frame take imports as an empty clip; span it to the next frame so its keys are kept.
            var clips = importer.defaultClipAnimations;
            foreach (var take in clips) take.lastFrame = Mathf.Max(take.lastFrame, take.firstFrame + 1);
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));
            if (clip == null) throw new InvalidOperationException("No rest pose take in " + path);
            return clip;
        }

        /// <summary>
        /// Writes the bone map and reference pose into the rig's importer and reimports. The reference is the one
        /// Unity generates for the file (arms put into a T), except the finger segments, which take their
        /// rotation from the rest pose take. With <paramref name="restReference"/> every bone takes the rest pose and
        /// the upper arms are then swung level into the T (<see cref="RestReference"/>).
        /// </summary>
        public static void Configure(ModelImporter importer, AnimationClip restPose, IReadOnlyDictionary<HumanBodyBones, string> bones = null,
            bool restReference = false)
        {
            var rest = FirstRotations(restPose);
            var description = importer.humanDescription;
            description.human = Array.Empty<HumanBone>();
            description.skeleton = Array.Empty<SkeletonBone>();
            importer.humanDescription = description;
            importer.SaveAndReimport();
            description = importer.humanDescription;
            var names = new HashSet<string>(description.skeleton.Select(s => s.name));
            if (!names.Contains(BoneName(bones, HumanBodyBones.Hips))) throw new InvalidOperationException("Unity generated no reference skeleton for " + importer.assetPath);
            var fingerBones = new HashSet<string>(FingerSlots().Select(slot => BoneName(bones, slot)).Where(bone => bone != null));
            foreach (string bone in fingerBones)
                if (names.Contains(bone) && !rest.ContainsKey(bone))
                    throw new InvalidOperationException("Rest pose take has no rotation for " + bone);
            for (int i = 0; i < description.skeleton.Length; i++)
                if (fingerBones.Contains(description.skeleton[i].name)) description.skeleton[i].rotation = rest[description.skeleton[i].name];
            if (restReference) RestReference(importer, description.skeleton, restPose, bones);
            var human = new List<HumanBone>();
            for (var slot = HumanBodyBones.Hips; slot < HumanBodyBones.LastBone; slot++)
            {
                string bone = BoneName(bones, slot);
                if (bone != null && names.Contains(bone))
                    human.Add(new HumanBone { humanName = HumanTrait.BoneName[(int)slot], boneName = bone, limit = new HumanLimit { useDefaultValues = true } });
            }
            description.human = human.ToArray();
            importer.humanDescription = description;
            importer.SaveAndReimport();
        }

        /// <summary>
        /// A takes file has no rest of its own, so Unity builds its reference from the first take's first frame, and
        /// whatever that frame holds (a stance's spine curl, head nod, knee bend) reads as zero muscle and is lost on
        /// every other body. Here the reference takes the rest pose take's rotations instead (bone lengths stay the
        /// file's: the rest take keys them a few mm off the takes, which Unity reports as a rig mis-match), with each
        /// upper arm then swung, as little as it takes, to point its forearm straight out to its side level: the T that
        /// Unity generates for a body whose own rest is this pose.
        /// </summary>
        private static void RestReference(ModelImporter importer, SkeletonBone[] skeleton, AnimationClip restPose, IReadOnlyDictionary<HumanBodyBones, string> bones)
        {
            var rotations = FirstRotations(restPose);
            var instance = (GameObject)UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(importer.assetPath));
            try
            {
                var transforms = instance.GetComponentsInChildren<Transform>(true).Where(t => t != instance.transform)
                    .GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
                foreach (var bone in skeleton)
                    if (transforms.TryGetValue(bone.name, out var t))
                    {
                        t.localPosition = bone.position;
                        t.localRotation = rotations.TryGetValue(bone.name, out var r) ? r : bone.rotation;
                    }
                for (var slot = HumanBodyBones.Hips; slot < HumanBodyBones.LastBone; slot++)
                {
                    string name = BoneName(bones, slot);
                    if (name != null && transforms.ContainsKey(name) && !rotations.ContainsKey(name))
                        throw new InvalidOperationException("Rest pose take has no rotation for " + name);
                }
                Transform Slot(HumanBodyBones slot) => transforms[BoneName(bones, slot)];
                Vector3 up = Vector3.up;   // the model stands on world up; its root carries the file's axis turn
                Vector3 right = Vector3.ProjectOnPlane(Slot(HumanBodyBones.RightUpperArm).position - Slot(HumanBodyBones.LeftUpperArm).position, up).normalized;
                foreach (var (arm, forearm, side) in new[] { (HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, 1f), (HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, -1f) })
                {
                    Transform upper = Slot(arm);
                    upper.rotation = Quaternion.FromToRotation(Slot(forearm).position - upper.position, right * side) * upper.rotation;
                }
                for (int i = 0; i < skeleton.Length; i++)
                    if (transforms.TryGetValue(skeleton[i].name, out var t))
                        skeleton[i].rotation = t.localRotation;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// Throws unless every finger slot is mapped to its bone (with a map: each finger the map names) and the
        /// avatar's reference pose has straight fingers: each segment's long axis in line with the one before it.
        /// (Muscle 0 is not that pose: Unity's zero is a relaxed curl, and straight reads as about +0.7 stretch on
        /// every finger joint.)
        /// </summary>
        public static void CheckFingers(Avatar avatar, GameObject model, IReadOnlyDictionary<HumanBodyBones, string> bones = null)
        {
            var instance = (GameObject)UnityEngine.Object.Instantiate(model);
            try
            {
                var animator = instance.GetComponent<Animator>() ?? instance.AddComponent<Animator>();
                animator.avatar = avatar;
                var slots = FingerSlots().Where(slot => BoneName(bones, slot) != null).ToList();
                foreach (var slot in slots)
                {
                    var bone = animator.GetBoneTransform(slot);
                    if (bone == null || bone.name != BoneName(bones, slot))
                        throw new InvalidOperationException("Finger slot " + slot + " is mapped to " + (bone == null ? "nothing" : bone.name));
                }
                var reference = avatar.humanDescription.skeleton.ToDictionary(s => s.name, s => s.rotation);
                foreach (var t in instance.GetComponentsInChildren<Transform>(true))
                    if (t != instance.transform && reference.TryGetValue(t.name, out var rotation)) t.localRotation = rotation;
                var failures = new List<string>();
                foreach (string side in new[] { "Left", "Right" })
                    foreach (string finger in Fingers)
                    {
                        var chain = Segments.Select(s => (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), side + finger + s))
                            .Where(slots.Contains).Select(animator.GetBoneTransform).ToArray();
                        if (chain.Length < 2) continue;
                        // The bones share one local long axis (the exporter's bone axis); read it off the first segment.
                        Vector3 axis = (Quaternion.Inverse(chain[0].rotation) * (chain[1].position - chain[0].position)).normalized;
                        for (int i = 1; i < chain.Length; i++)
                        {
                            float bend = Vector3.Angle(chain[i - 1].rotation * axis, chain[i].rotation * axis);
                            if (bend > StraightTolerance) failures.Add(string.Format("{0} {1:F1} deg", chain[i].name, bend));
                        }
                    }
                if (failures.Count > 0) throw new InvalidOperationException("Reference pose fingers are not straight: " + string.Join(", ", failures));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static string BoneName(IReadOnlyDictionary<HumanBodyBones, string> bones, HumanBodyBones slot) =>
            bones == null ? slot.ToString() : bones.TryGetValue(slot, out string bone) ? bone : null;

        private static IEnumerable<HumanBodyBones> FingerSlots()
        {
            foreach (string side in new[] { "Left", "Right" })
                foreach (string finger in Fingers)
                    foreach (string segment in Segments)
                        yield return (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), side + finger + segment);
        }

        /// <summary>Each bone's local rotation at the start of the take, by bone name.</summary>
        private static Dictionary<string, Quaternion> FirstRotations(AnimationClip clip)
        {
            var result = new Dictionary<string, Quaternion>();
            foreach (var bone in AnimationUtility.GetCurveBindings(clip).Where(b => b.propertyName.StartsWith("m_LocalRotation.", StringComparison.Ordinal)).GroupBy(b => b.path))
            {
                float Component(string axis) => AnimationUtility.GetEditorCurve(clip, bone.First(b => b.propertyName == "m_LocalRotation." + axis)).Evaluate(0f);
                result[bone.Key.Substring(bone.Key.LastIndexOf('/') + 1)] = new Quaternion(Component("x"), Component("y"), Component("z"), Component("w")).normalized;
            }
            return result;
        }
    }
}
