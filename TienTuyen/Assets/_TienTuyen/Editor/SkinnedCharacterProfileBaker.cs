using System.Collections.Generic;
using TienTuyen.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace TienTuyen.Editor
{
    /// <summary>Editor measurements shared by the character importers.</summary>
    public static class SkinnedCharacterProfileBaker
    {
        /// <summary>
        /// Plays the profile's death clip on its own model exactly as the runtime driver does,
        /// skins every frame and returns the lift that keeps the lowest vertex on the floor (y=0).
        /// Zero while the body is clear, so the standing onset never floats.
        /// </summary>
        public static AnimationCurve MeasureDeathFloorLift(SkinnedCharacterProfile profile)
        {
            var curve = new AnimationCurve();
            if (profile == null || profile.model == null || profile.avatar == null || profile.death == null) return curve;
            var instance = Object.Instantiate(profile.model);
            instance.hideFlags = HideFlags.HideAndDontSave;
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var baked = new Mesh();
            var graph = PlayableGraph.Create("DeathFloorLift");
            try
            {
                var animator = instance.GetComponent<Animator>() ?? instance.AddComponent<Animator>();
                animator.runtimeAnimatorController = null;
                animator.avatar = profile.avatar;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind();
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var clip = AnimationClipPlayable.Create(graph, profile.death);
                clip.SetSpeed(0);
                clip.SetApplyFootIK(true);
                AnimationPlayableOutput.Create(graph, "Pose", animator).SetSourcePlayable(clip);
                var skins = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                int frames = Mathf.Max(1, Mathf.CeilToInt(profile.death.length * 30f));
                var lift = new float[frames + 1];
                for (int i = 0; i <= frames; i++)
                {
                    clip.SetTime(Mathf.Min(i / 30f, profile.death.length));
                    graph.Evaluate(0f);
                    float lowest = float.PositiveInfinity;
                    foreach (var skin in skins)
                    {
                        skin.BakeMesh(baked, true);
                        var t = skin.transform;
                        foreach (var v in baked.vertices) lowest = Mathf.Min(lowest, (t.position + t.rotation * v).y);
                    }
                    lift[i] = Mathf.Max(0f, -lowest);
                }
                // Hold each correction over its neighbours so a one-frame contact cannot pop.
                for (int i = 0; i <= frames; i++)
                {
                    float held = 0f;
                    for (int j = Mathf.Max(0, i - 2); j <= Mathf.Min(frames, i + 2); j++) held = Mathf.Max(held, lift[j]);
                    curve.AddKey(new Keyframe(i / 30f, held));
                }
                for (int i = 0; i < curve.length; i++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                    AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                }
                return curve;
            }
            finally
            {
                if (graph.IsValid()) graph.Destroy();
                Object.DestroyImmediate(baked);
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// Outlines each shoe sole on the bind pose: the lowest skinned vertices owned by the foot
        /// or toe bone, reduced to their extremes in sixteen directions and stored in that bone's
        /// space. The runtime driver keeps every point above the floor whatever the foot's tilt.
        /// </summary>
        public static void MeasureSoles(SkinnedCharacterProfile profile)
        {
            profile.leftSole = new SolePoint[0];
            profile.rightSole = new SolePoint[0];
            if (profile.model == null || profile.avatar == null || !profile.avatar.isHuman) return;
            var instance = Object.Instantiate(profile.model);
            instance.hideFlags = HideFlags.HideAndDontSave;
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var baked = new Mesh();
            try
            {
                var animator = instance.GetComponent<Animator>() ?? instance.AddComponent<Animator>();
                animator.runtimeAnimatorController = null;
                animator.avatar = profile.avatar;
                animator.Rebind();
                var leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                var leftToes = animator.GetBoneTransform(HumanBodyBones.LeftToes);
                var rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
                var rightToes = animator.GetBoneTransform(HumanBodyBones.RightToes);
                var left = new List<(Vector3 world, Transform bone)>();
                var right = new List<(Vector3 world, Transform bone)>();
                foreach (var skin in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var mesh = skin.sharedMesh;
                    if (mesh == null) continue;
                    skin.BakeMesh(baked, true);
                    var vertices = baked.vertices;
                    var weights = mesh.boneWeights;
                    var bones = skin.bones;
                    if (weights.Length != vertices.Length) continue;
                    var t = skin.transform;
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        int index = weights[i].boneIndex0;
                        if (index < 0 || index >= bones.Length) continue;
                        var bone = bones[index];
                        var world = t.position + t.rotation * vertices[i];
                        if (bone == leftFoot || (bone == leftToes && bone != null)) left.Add((world, bone));
                        else if (bone == rightFoot || (bone == rightToes && bone != null)) right.Add((world, bone));
                    }
                }
                profile.leftSole = Outline(left, leftToes);
                profile.rightSole = Outline(right, rightToes);
            }
            finally
            {
                Object.DestroyImmediate(baked);
                Object.DestroyImmediate(instance);
            }
        }

        private static SolePoint[] Outline(List<(Vector3 world, Transform bone)> vertices, Transform toes)
        {
            if (vertices.Count == 0) return new SolePoint[0];
            float lowest = float.PositiveInfinity;
            foreach (var v in vertices) lowest = Mathf.Min(lowest, v.world.y);
            // The tread band: anything within 4 cm of the lowest vertex (sole edge, heel and toe
            // cap) can reach the floor once the clip tilts the foot.
            var tread = vertices.FindAll(v => v.world.y < lowest + .04f);
            var points = new List<SolePoint>();
            var chosen = new HashSet<int>();
            for (int k = 0; k < 16; k++)
            {
                float angle = k * Mathf.PI / 8f;
                var direction = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                int best = 0;
                for (int i = 1; i < tread.Count; i++)
                    if (Vector3.Dot(tread[i].world, direction) > Vector3.Dot(tread[best].world, direction)) best = i;
                if (!chosen.Add(best)) continue;
                var bone = tread[best].bone;
                points.Add(new SolePoint { toes = bone == toes, local = bone.InverseTransformPoint(tread[best].world) });
            }
            return points.ToArray();
        }
    }
}
