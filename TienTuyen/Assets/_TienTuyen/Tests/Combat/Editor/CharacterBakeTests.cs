using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TienTuyen.Presentation;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TienTuyen.Combat.Tests
{
    /// <summary>The clips baked from the CS:GO takes for the ValveBiped bodies.</summary>
    public sealed class CharacterBakeTests
    {
        [Test]
        public void BakedStandIdleKeepsTheSourceTakesSpineHeadAndKnees()
        {
            // The CS:GO stand_idle curls the spine, nods the head toward the sights and bends the knees. An xdR avatar whose
            // reference is that posed frame reads all three as zero and the bodies stand straight; the bake must keep them.
            // Measured: rest reference within 5 (knees; the hero's own rest bends its legs), 2.4 (chest) and 4.7 (head);
            // posed reference off by 34, 5.1 and 45.
            const float MaxKnee = 8f, MaxHead = 10f, MaxChestTilt = 4f;
            const string Takes = "Assets/_TienTuyen/Art/Animations/xdR/xdr_ak.fbx";
            var source = AssetDatabase.LoadAllAssetsAtPath(Takes);
            var pose = Measure(AssetDatabase.LoadAssetAtPath<GameObject>(Takes), source.OfType<Avatar>().First(), source.OfType<AnimationClip>().First(c => c.name == "stand_idle"));
            var failures = new List<string>();
            foreach (var path in new[] { "Characters/PAVN/PAVN_ValveProfile", "Characters/Enemies/ShooterValveProfile", "Characters/Enemies/EliteValveProfile" })
            {
                var profile = Resources.Load<SkinnedCharacterProfile>(path);
                var baked = Measure(profile.model, profile.avatar, profile.weaponSets[0].hold);
                void Check(string what, float got, float want, float max)
                {
                    if (Mathf.Abs(got - want) > max) failures.Add($"{profile.name} {what} {got:F1} (source {want:F1})");
                }
                Check("left knee bend", baked.leftKnee, pose.leftKnee, MaxKnee);
                Check("right knee bend", baked.rightKnee, pose.rightKnee, MaxKnee);
                Check("chest tilt", baked.chestTilt, pose.chestTilt, MaxChestTilt);
                float head = Quaternion.Angle(baked.headOnChest, pose.headOnChest);
                if (head > MaxHead) failures.Add($"{profile.name} head on chest {head:F1} from the source");
            }
            Assert.That(failures, Is.Empty);
        }

        /// <summary>
        /// A clip's first frame on a body: knee bends (degrees between thigh and shin), the chest's tilt from vertical (Spine4 to
        /// Neck1) and the head's turn on the chest. ValveBiped bodies share the bone axes, so the turns compare across bodies.
        /// </summary>
        private static (float leftKnee, float rightKnee, float chestTilt, Quaternion headOnChest) Measure(GameObject model, Avatar avatar, AnimationClip clip)
        {
            var instance = Object.Instantiate(model);
            try
            {
                var animator = instance.GetComponent<Animator>();
                if (animator == null) animator = instance.AddComponent<Animator>();
                animator.avatar = avatar;
                clip.SampleAnimation(instance, 0f);
                var bones = instance.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
                Transform B(string name) => bones["ValveBiped.Bip01_" + name];
                float Knee(string side) => Vector3.Angle(B(side + "_Calf").position - B(side + "_Thigh").position, B(side + "_Foot").position - B(side + "_Calf").position);
                return (Knee("L"), Knee("R"), Vector3.Angle(B("Neck1").position - B("Spine4").position, Vector3.up),
                    Quaternion.Inverse(B("Spine4").rotation) * B("Head1").rotation);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
    }
}
