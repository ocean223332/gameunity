using UnityEngine;

namespace TienTuyen.Duel.Presentation
{
    /// <summary>
    /// A duel body's reaction to being shot, laid over the pose its rig has just written. A hit flinches the upper body
    /// and the head, the way a head hit or a body hit does (HL2's flinch_head_01 and flinch_stomach_01 gestures, as the
    /// frame-by-frame turn each adds to its first frame, at the half weight their QC gives them). The torso's turn goes on
    /// the lowest spine bone, and the gun in hand (which the rig places outside the skeleton) turns with it about the same
    /// point, so the hands stay on it; the head's goes on the neck. A falling body leaves its last living pose over <see cref="DeathBlend"/> (the fall starts
    /// standing, so a crouched or aiming body would otherwise pop up first), and is kept out of the floor: its death clip
    /// is not the one the profile's floor lift was measured on, so the lift is measured here, on the skins, while it falls.
    /// </summary>
    internal sealed class DuelHitReaction
    {
        private const float Fps = 30f, Weight = .5f;
        // How fast a floor lift that is no longer needed settles back (m/s), so a contact that comes and goes cannot pop.
        private const float LiftSettle = .5f;
        private const float DeathBlend = .3f;

        private readonly Transform facing, floor, spine, neck, hips;
        private readonly SkinnedMeshRenderer[] skins;
        private readonly Transform[] bones;
        private readonly Quaternion[] living;
        private Vector3 livingHips;
        private float dead = -1f, fallY;
        private Mesh baked;
        private Quaternion[] torso, head;
        private float time = -1f, lift;

        public DuelHitReaction(Transform root)
        {
            facing = root;
            floor = Find(root, "CharacterPose") ?? root;
            spine = Find(root, "ValveBiped.Bip01_Spine");
            neck = Find(root, "ValveBiped.Bip01_Neck1");
            hips = Find(root, "ValveBiped.Bip01_Pelvis");
            skins = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            bones = System.Array.FindAll(root.GetComponentsInChildren<Transform>(true), t => t.name.StartsWith("ValveBiped.", System.StringComparison.Ordinal));
            living = new Quaternion[bones.Length];
        }

        /// <summary>Starts the flinch of a head or a body hit; a second hit restarts it.</summary>
        public void Hurt(bool headHit)
        {
            torso = headHit ? HeadTorso : BodyTorso;
            head = headHit ? HeadHead : BodyHead;
            time = 0f;
        }

        /// <summary>Called after the rig has posed the body this frame.</summary>
        /// <param name="held">The gun in hand this frame, or null (knife, none).</param>
        public void Apply(float deltaTime, bool dying, Transform held)
        {
            if (dying)
            {
                time = -1f;
                dead = dead < 0f ? 0f : dead + deltaTime;
                LeaveLiving();
                NeverRise();
                KeepOffFloor(deltaTime);
                return;
            }
            lift = 0f;
            dead = -1f;
            Flinch(deltaTime, held);
            for (int i = 0; i < bones.Length; i++) living[i] = bones[i].localRotation;
            if (hips != null) livingHips = hips.localPosition;
        }

        private void Flinch(float deltaTime, Transform held)
        {
            if (time < 0f || spine == null) return;
            float frame = time * Fps;
            time += deltaTime;
            if (frame >= torso.Length - 1) { time = -1f; return; }
            Quaternion toWorld = facing.rotation, toCharacter = Quaternion.Inverse(toWorld);
            Quaternion chest = toWorld * Sample(torso, frame) * toCharacter;
            spine.rotation = chest * spine.rotation;
            if (held != null) held.SetPositionAndRotation(spine.position + chest * (held.position - spine.position), chest * held.rotation);
            if (neck != null) neck.rotation = chest * toWorld * Sample(head, frame) * toCharacter * Quaternion.Inverse(chest) * neck.rotation;
        }

        /// <summary>The first <see cref="DeathBlend"/> of the fall eases out of the pose the body last had alive.</summary>
        private void LeaveLiving()
        {
            if (dead >= DeathBlend) return;
            float w = Mathf.SmoothStep(0f, 1f, dead / DeathBlend);
            for (int i = 0; i < bones.Length; i++) bones[i].localRotation = Quaternion.Slerp(living[i], bones[i].localRotation, w);
            if (hips != null) hips.localPosition = Vector3.Lerp(livingHips, hips.localPosition, w);
        }

        /// <summary>A dying body does not go up: the pelvis holds or falls, from the pose it died in (a crouch
        /// would otherwise rise toward the standing start of the fall).</summary>
        private void NeverRise()
        {
            if (hips == null) return;
            float y = hips.position.y - floor.position.y;
            if (dead > 0f && y > fallY) hips.position += Vector3.up * (fallY - y);
            else fallY = y;
        }

        private static Quaternion Sample(Quaternion[] frames, float frame)
        {
            int at = Mathf.FloorToInt(frame);
            Quaternion turn = Quaternion.Slerp(frames[at], frames[at + 1], frame - at);
            return Quaternion.Slerp(Quaternion.identity, turn, Weight);
        }

        private void KeepOffFloor(float deltaTime)
        {
            if (hips == null || skins.Length == 0) return;
            if (baked == null) baked = new Mesh { hideFlags = HideFlags.DontSave };
            float lowest = float.PositiveInfinity;
            foreach (var skin in skins)
            {
                if (!skin.gameObject.activeInHierarchy) continue;
                skin.BakeMesh(baked, true);
                var t = skin.transform;
                foreach (var v in baked.vertices) lowest = Mathf.Min(lowest, (t.position + t.rotation * v).y);
            }
            if (float.IsInfinity(lowest)) return;
            lift = Mathf.Max(floor.position.y - lowest, lift - LiftSettle * deltaTime, 0f);
            hips.position += Vector3.up * lift;
        }

        private static Transform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        // Generated by output/us-slot1/hitreact/flinch_deltas.py from the GMod animation sources (flinching/):
        // each frame's turn in the character's frame (x right, y up, z forward), at full weight.
        // flinching/head_01.smd: 16 frames at 30 fps.
        private static readonly Quaternion[] HeadTorso =
        {
            new Quaternion(-0.00000f, -0.00000f, 0.00000f, 1.00000f),
            new Quaternion(-0.06343f, -0.04236f, 0.00038f, 0.99709f),
            new Quaternion(-0.12921f, -0.10002f, -0.00711f, 0.98653f),
            new Quaternion(-0.12534f, -0.10723f, -0.00897f, 0.98626f),
            new Quaternion(-0.10751f, -0.10582f, -0.01151f, 0.98849f),
            new Quaternion(-0.08105f, -0.10146f, -0.01503f, 0.99142f),
            new Quaternion(-0.05070f, -0.09521f, -0.01912f, 0.99398f),
            new Quaternion(-0.02084f, -0.08753f, -0.02315f, 0.99568f),
            new Quaternion(0.00451f, -0.07865f, -0.02633f, 0.99654f),
            new Quaternion(0.02181f, -0.06878f, -0.02771f, 0.99701f),
            new Quaternion(0.03078f, -0.05808f, -0.02703f, 0.99747f),
            new Quaternion(0.03382f, -0.04676f, -0.02470f, 0.99803f),
            new Quaternion(0.03163f, -0.03494f, -0.02073f, 0.99867f),
            new Quaternion(0.02476f, -0.02277f, -0.01509f, 0.99932f),
            new Quaternion(0.01361f, -0.01050f, -0.00782f, 0.99982f),
            new Quaternion(-0.00000f, -0.00000f, 0.00000f, 1.00000f)
        };
        private static readonly Quaternion[] HeadHead =
        {
            new Quaternion(-0.00000f, 0.00000f, 0.00000f, 1.00000f),
            new Quaternion(-0.06814f, -0.00066f, -0.01588f, 0.99755f),
            new Quaternion(-0.13884f, -0.01117f, -0.03043f, 0.98978f),
            new Quaternion(0.02791f, -0.04849f, 0.00152f, 0.99843f),
            new Quaternion(0.18593f, -0.07062f, 0.02953f, 0.97958f),
            new Quaternion(0.24687f, -0.05978f, 0.04685f, 0.96607f),
            new Quaternion(0.26832f, -0.01196f, 0.07963f, 0.95996f),
            new Quaternion(0.25794f, 0.06669f, 0.12710f, 0.95544f),
            new Quaternion(0.22057f, 0.15940f, 0.17003f, 0.94712f),
            new Quaternion(0.16360f, 0.24306f, 0.19231f, 0.93658f),
            new Quaternion(0.10259f, 0.27953f, 0.18296f, 0.93694f),
            new Quaternion(0.06632f, 0.26107f, 0.15470f, 0.95053f),
            new Quaternion(0.04352f, 0.22302f, 0.12617f, 0.96564f),
            new Quaternion(0.02759f, 0.16533f, 0.09236f, 0.98152f),
            new Quaternion(0.01396f, 0.08728f, 0.04940f, 0.99486f),
            new Quaternion(-0.00000f, 0.00000f, 0.00000f, 1.00000f)
        };
        // flinching/stomach_01.smd: 19 frames at 30 fps.
        private static readonly Quaternion[] BodyTorso =
        {
            new Quaternion(-0.00000f, -0.00000f, 0.00000f, 1.00000f),
            new Quaternion(0.01186f, -0.02759f, 0.00013f, 0.99955f),
            new Quaternion(0.03539f, -0.08099f, 0.00232f, 0.99608f),
            new Quaternion(0.05242f, -0.11615f, 0.00535f, 0.99183f),
            new Quaternion(0.06036f, -0.11960f, 0.00647f, 0.99096f),
            new Quaternion(0.06119f, -0.11543f, 0.00633f, 0.99141f),
            new Quaternion(0.05755f, -0.10810f, 0.00553f, 0.99246f),
            new Quaternion(0.05026f, -0.09890f, 0.00432f, 0.99382f),
            new Quaternion(0.03961f, -0.08841f, 0.00291f, 0.99529f),
            new Quaternion(0.02979f, -0.07696f, 0.00177f, 0.99659f),
            new Quaternion(0.02275f, -0.06479f, 0.00103f, 0.99764f),
            new Quaternion(0.01719f, -0.05207f, 0.00053f, 0.99850f),
            new Quaternion(0.01267f, -0.03895f, 0.00022f, 0.99916f),
            new Quaternion(0.00894f, -0.02567f, 0.00005f, 0.99963f),
            new Quaternion(0.00588f, -0.01423f, -0.00002f, 0.99988f),
            new Quaternion(0.00344f, -0.00640f, -0.00002f, 0.99997f),
            new Quaternion(0.00161f, -0.00201f, -0.00001f, 1.00000f),
            new Quaternion(0.00043f, -0.00028f, -0.00000f, 1.00000f),
            new Quaternion(-0.00000f, -0.00000f, 0.00000f, 1.00000f)
        };
        private static readonly Quaternion[] BodyHead =
        {
            new Quaternion(-0.00000f, 0.00000f, 0.00000f, 1.00000f),
            new Quaternion(0.01918f, 0.01389f, 0.01606f, 0.99959f),
            new Quaternion(0.06051f, 0.04436f, 0.05447f, 0.99569f),
            new Quaternion(0.10790f, 0.08090f, 0.10673f, 0.98510f),
            new Quaternion(0.14952f, 0.11656f, 0.16459f, 0.96797f),
            new Quaternion(0.16940f, 0.14377f, 0.20846f, 0.95246f),
            new Quaternion(0.15932f, 0.15820f, 0.21809f, 0.94975f),
            new Quaternion(0.14140f, 0.16935f, 0.21594f, 0.95116f),
            new Quaternion(0.11782f, 0.17643f, 0.20420f, 0.95566f),
            new Quaternion(0.09033f, 0.17837f, 0.18511f, 0.96216f),
            new Quaternion(0.06058f, 0.17410f, 0.16095f, 0.96959f),
            new Quaternion(0.03025f, 0.16271f, 0.13393f, 0.97707f),
            new Quaternion(0.00117f, 0.14360f, 0.10611f, 0.98393f),
            new Quaternion(-0.02451f, 0.11648f, 0.07922f, 0.98973f),
            new Quaternion(-0.04129f, 0.08421f, 0.05451f, 0.99410f),
            new Quaternion(-0.04290f, 0.05146f, 0.03260f, 0.99722f),
            new Quaternion(-0.02903f, 0.02307f, 0.01475f, 0.99920f),
            new Quaternion(-0.00921f, 0.00529f, 0.00348f, 0.99994f),
            new Quaternion(-0.00000f, 0.00000f, 0.00000f, 1.00000f)
        };
    }
}
