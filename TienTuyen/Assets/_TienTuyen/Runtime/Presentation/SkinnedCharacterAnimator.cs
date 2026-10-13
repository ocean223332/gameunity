using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace TienTuyen.Presentation
{
    /// <summary>
    /// Clip-driven pose for a skinned Humanoid, owned by <see cref="CombatCharacterRig"/>.
    /// A manual PlayableGraph (evaluated only from Tick, so pause freezes it) mixes 4-way
    /// locomotion on one shared gait phase with a masked upper-body layer (aim / reload / hit /
    /// melee), full-body charge actions and a one-shot death. Action clip times come from the
    /// caller every tick; only their short fades are kept here. After evaluation the pose is corrected in place: hips turn toward
    /// fast travel the clips cannot keep up with (bounded torso twist), the weapon is placed on the Weapon bone its clip set
    /// animates, or else on its <see cref="WeaponHold"/> frame (low-ready across the chest, or shouldered with the bore on
    /// the aim), both arms are solved onto its grips with anatomical wrists (the forearm carries most of
    /// the hand's twist; flexion and deviation stay in range), and feet are lifted out of the ground.
    /// </summary>
    internal sealed class SkinnedCharacterAnimator
    {
        private const float MaxTorsoTwist = 60f;
        private const float MaxLegYaw = 60f;
        private const float DirectionBlendTime = .12f;
        // Approved wrist range relative to the forearm (degrees). The supporting hand is supinated
        // under the fore-end, so its wrist keeps more of the twist and spares the elbow.
        private const float MaxFlexion = 45f, MaxDeviation = 20f, GripTwist = 30f, SupportTwist = 60f;
        // Shouldered aim: the chest follows part of the aim pitch, the neck and head the rest (the cheek stays on
        // the comb); the bore follows all of it.
        private const float MaxAimRise = 10f, MaxAimDrop = 15f, ChestAimShare = .6f;
        // Largest turn of the shouldered bore from the shoulder aim line onto the crosshair point.
        private const float MaxBoreTurn = 8f;
        private const float RecoilKick = .03f, RecoilRise = 5f, RecoilChest = 2.5f;
        // A draw: the weapon dips this far (degrees, about the right shoulder) by DrawDipIn of the draw, stays low until
        // DrawDipOut, then rises to the ready by the end. Both hands stay on it.
        private const float DrawDip = 40f, DrawDipIn = .15f, DrawDipOut = .4f;
        // Share of the arms the generic hit clip takes from a carried weapon's clips.
        private const float CarriedHitShare = .5f;
        // A fist-held blade leans this far (tan) from the thumb side toward the knuckles.
        private const float BladeLean = .35f;
        // Most a clavicle turns (degrees) to bring a grip-solved hand within reach.
        private const float MaxShoulderReach = 30f;
        // Bend (degrees) of each finger joint from bind, root to tip, in a fist round a blade's handle.
        private static readonly Vector3 FistCurl = new Vector3(75f, 90f, 55f), ThumbCurl = new Vector3(15f, 30f, 25f);

        private enum Loco { Idle, Forward, Backward, Left, Right }

        /// <summary>
        /// An arm at bind: rotations the solve starts from, the forearm twist axis and wrist axes, and the
        /// hand frame the hold data is written in (knuckles, thumb, palm), so any Humanoid can use it.
        /// </summary>
        private sealed class Arm
        {
            public Transform upper, lower, hand, socket;
            public Quaternion upperBind, lowerBind, handBind, handFrameInverse;
            public Vector3 twistAxis, flexAxis, devAxis;
            public float handScale, keep;
            public Digit[] digits;
            /// <summary>
            /// Forearm roll bone (child of the lower arm, not a Humanoid slot, e.g. Valve's Ulna) that a skin is bound to, its bind
            /// and its long axis; null without one. With one the lower arm stays a hinge and this bone takes the forearm's roll.
            /// </summary>
            public Transform twist;
            public Quaternion twistBind;
            public Vector3 twistLong;
            /// <summary>Valve's wrist helper (child of the lower arm on the hand joint) and its and the hand's local binds; null without one.</summary>
            public Transform wrist;
            public Quaternion wristBind, handLocalBind;
        }

        private struct Digit
        {
            public Transform bone;
            public Quaternion bind;
            public Vector3 flex, across;
            public int joint;
            public float share;
            public bool thumb, index;
        }

        private readonly SkinnedCharacterProfile profile;
        private readonly Transform pose;
        private readonly Animator animator;
        private PlayableGraph graph;
        // Standing locomotion clips (Loco order), then the crouched ones; the speeds the legs carry are blended by the crouch.
        private const int LocoCount = 5;
        private readonly AnimationClipPlayable[] loco = new AnimationClipPlayable[LocoCount * 2];
        private readonly float[] locoLength = new float[LocoCount * 2];
        private readonly float[] postureSpeed = new float[LocoCount * 2];
        private readonly float[] locoSpeed = new float[LocoCount];
        // 0 standing to 1 crouched (the game's crouch, eased).
        private float crouch;
        private AnimationMixerPlayable locoMixer, bodyMixer, upperMixer;
        private AnimationLayerMixerPlayable layers;
        private AnimationClipPlayable aimPlayable, reloadPlayable, hitPlayable, deathPlayable, meleePlayable;
        private AnimationMixerPlayable chargeMixer;
        private readonly AnimationClipPlayable[] charge = new AnimationClipPlayable[3];
        private readonly float[] chargeWeights = new float[3];
        private readonly bool hasAim, hasReload, hasHit, hasDeath, hasMelee;
        private readonly bool[] hasCharge = new bool[3];
        private const float ActionFadeTime = .08f;

        private readonly Transform hips, spine, chest, upperChest;
        private readonly Transform leftUpperArm, leftLowerArm, leftHand, rightUpperArm, rightLowerArm, rightHand;
        private readonly Transform leftUpperLeg, leftLowerLeg, leftFoot, leftToes, rightUpperLeg, rightLowerLeg, rightFoot, rightToes;
        private readonly Transform gripSocket, supportSocket;
        private struct FistJoint { public Transform bone; public Quaternion bind, curl; }
        // Ring and little finger bones no clip drives, with their middle-finger leader (HandGripData.Leader), read at bind.
        private struct Follower { public Transform bone, leader; public Quaternion bind, leaderUnbind; }
        private readonly Follower[] followers;
        private readonly FistJoint[] fist = new FistJoint[0];
        private readonly Transform[] spineChain, headChain;
        private readonly float[] weights = new float[5];
        private readonly float[] targetWeights = new float[5];
        private AvatarMask ownedMask, hitMask;
        private AnimationClipPlayable carriedHitPlayable;
        private readonly float footHeight, toeHeight;
        private readonly SolePoint[] leftSole, rightSole;

        private float phase, idleTime, legYaw, reloadWeight, hitTime = -1f, lastHit, deathTime, deathWeight;
        private float meleeWeight, chargeWeight, aimShare, reloadShare, hitWeight, forwardRun, holdAim;
        private bool dying, anchorKnown, gripKnown, directionKnown, disposed;
        private Vector3 aimAnchor;
        private Quaternion rightGrip = Quaternion.identity, leftGrip = Quaternion.identity;
        private Vector3 weaponUpInHand = Vector3.up;
        private readonly Arm rightArm, leftArm;
        private readonly Quaternion chestBindInverse;
        private readonly float shoulderWidth;

        // Weapons on clip sets (profile.weaponSets): hold, aim, run, fire and reload clips per set, standing then crouched
        // (PostureSlots each); the weapon rides the Weapon bone.
        private const int PostureSlots = 8, Postures = 2, CarrySlots = PostureSlots * Postures;
        private const int HoldSlot = 0, AimSlot = 1, RunSlot = 2, FireSlot = 3, ReloadSlot = 4, ReloadStartSlot = 5, ReloadInsertSlot = 6, ReloadEndSlot = 7;
        private readonly WeaponClipSet[] sets;
        private readonly Transform weaponBone, rightTarget, leftTarget;
        private AnimationMixerPlayable carryMixer;
        private readonly AnimationClipPlayable[] carryClips;
        private int carrySet = -1;
        private float fireTime = -1f, fireWeight, drawProgress = -1f, drawDip, carryMoving, reloadSeconds;
        // A reload by the round: the game's start and end phases (seconds); zero for a whole reload.
        private Vector2 reloadPhases;
        // Share of the reload each slot plays (whole, start, insert, end), in each posture.
        private readonly float[] reloadParts = { 1f, 0f, 0f, 0f };
        // Reload slots across both postures (standing whole, start, insert, end, then crouched).
        private const int ReloadTracks = 4 * Postures;
        // Seconds the insert loop crossfades into the end.
        private const float ShellEndBlend = .1f;
        // Finger bones of the sets' grip data, by name and per delta list (resolved once).
        private System.Collections.Generic.Dictionary<string, Transform> bonesByName;
        private readonly System.Collections.Generic.Dictionary<object, Transform[]> fingerBones = new System.Collections.Generic.Dictionary<object, Transform[]>();
        private readonly System.Collections.Generic.Dictionary<Transform, Transform> magazineMounts = new System.Collections.Generic.Dictionary<Transform, Transform>();
        // The shell the left hand carries to a tube-fed gun's loading port (made on first use).
        private MeshFilter heldShell;

        public Transform LeftFoot => leftFoot;
        public Transform RightFoot => rightFoot;
        public Transform GripSocket => gripSocket;
        public Transform SupportSocket => supportSocket;
        public Transform Chest => upperChest != null ? upperChest : chest != null ? chest : spine;
        public float GaitPhase => phase * Mathf.PI * 2f;
        public bool Dying => dying;
        public bool DeathFinished => dying && (!hasDeath || deathTime >= profile.death.length);
        public float LegYaw => legYaw;
        /// <summary>0..1 influence of any melee or charge action clip on the pose.</summary>
        public float ActionWeight => Mathf.Max(meleeWeight, chargeWeight);
        /// <summary>The held weapon rides the Weapon bone on its clip set (not the fitted <see cref="WeaponHold"/>).</summary>
        public bool Carrying => carrySet >= 0;

        /// <summary>Index of the profile's clip set for a game weapon id, or -1 (also when the model has no weapon bones).</summary>
        public int FindWeaponSet(string weapon)
        {
            for (int i = 0; weapon != null && leftTarget != null && i < sets.Length; i++)
                if (sets[i] != null && sets[i].weapon == weapon) return i;
            return -1;
        }

        /// <summary>Clip set of the weapon now in the hands; -1 for one without a set.</summary>
        public void Carry(int set) => carrySet = set >= 0 && set < sets.Length ? set : -1;

        /// <summary>A shot left the carried weapon: its fire clip restarts.</summary>
        public void Fired() => fireTime = 0f;

        /// <summary>Clip set <paramref name="set"/>'s world model, or null.</summary>
        public GameObject WeaponModel(int set) => set >= 0 && set < sets.Length ? sets[set].model : null;
        public HandGripData GripData(int set) => set >= 0 && set < sets.Length ? sets[set].grip : null;

        /// <summary>
        /// Seconds the game's current reload lasts, and for a reload by the round its start and end phases (seconds; zero
        /// otherwise). They time a shell-by-shell reload.
        /// </summary>
        public void ReloadLength(float seconds, Vector2 phases)
        {
            reloadSeconds = seconds;
            reloadPhases = phases;
        }

        /// <summary>Progress 0..1 of drawing the carried weapon (the game's switch timing); negative when not drawing.</summary>
        public void Draw(float progress) => drawProgress = progress;

        /// <summary>How far down the body is, 0 standing to 1 crouched: the crouched clips blend in by it.</summary>
        public void Crouch(float amount) => crouch = Mathf.Clamp01(amount);

        public SkinnedCharacterAnimator(SkinnedCharacterProfile profile, Transform pose, Transform model)
        {
            this.profile = profile;
            this.pose = pose;
            animator = model.GetComponent<Animator>() ?? model.gameObject.AddComponent<Animator>();
            animator.runtimeAnimatorController = null;
            animator.avatar = profile.avatar;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.keepAnimatorStateOnDisable = true;
            animator.Rebind();

            hips = Bone(HumanBodyBones.Hips); spine = Bone(HumanBodyBones.Spine);
            chest = Bone(HumanBodyBones.Chest); upperChest = Bone(HumanBodyBones.UpperChest);
            leftUpperArm = Bone(HumanBodyBones.LeftUpperArm); leftLowerArm = Bone(HumanBodyBones.LeftLowerArm); leftHand = Bone(HumanBodyBones.LeftHand);
            rightUpperArm = Bone(HumanBodyBones.RightUpperArm); rightLowerArm = Bone(HumanBodyBones.RightLowerArm); rightHand = Bone(HumanBodyBones.RightHand);
            leftUpperLeg = Bone(HumanBodyBones.LeftUpperLeg); leftLowerLeg = Bone(HumanBodyBones.LeftLowerLeg);
            leftFoot = Bone(HumanBodyBones.LeftFoot); leftToes = Bone(HumanBodyBones.LeftToes);
            rightUpperLeg = Bone(HumanBodyBones.RightUpperLeg); rightLowerLeg = Bone(HumanBodyBones.RightLowerLeg);
            rightFoot = Bone(HumanBodyBones.RightFoot); rightToes = Bone(HumanBodyBones.RightToes);
            var chain = new System.Collections.Generic.List<Transform>(3);
            foreach (var bone in new[] { spine, chest, upperChest }) if (bone != null) chain.Add(bone);
            spineChain = chain.ToArray();
            chain.Clear();
            foreach (var bone in new[] { Bone(HumanBodyBones.Neck), Bone(HumanBodyBones.Head) }) if (bone != null) chain.Add(bone);
            headChain = chain.ToArray();

            // Sole clearance from the bind pose, before any clip is applied (feet at model y=0).
            float ground = model.position.y;
            footHeight = (leftFoot.position.y + rightFoot.position.y) * .5f - ground;
            toeHeight = leftToes != null && rightToes != null ? (leftToes.position.y + rightToes.position.y) * .5f - ground : footHeight;
            leftSole = profile.leftSole ?? new SolePoint[0];
            rightSole = profile.rightSole ?? new SolePoint[0];
            gripSocket = PalmSocket("GripHand", rightHand, HumanBodyBones.RightMiddleProximal);
            supportSocket = PalmSocket("SupportHand", leftHand, HumanBodyBones.LeftMiddleProximal);
            if (profile.fistChannel != Vector2.zero)
            {
                FistSocket(gripSocket, rightHand, HumanBodyBones.RightIndexProximal, HumanBodyBones.RightRingProximal, HumanBodyBones.RightThumbProximal);
                FistSocket(supportSocket, leftHand, HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftThumbProximal);
            }
            if (profile.guardHold) fist = BindFist();
            followers = Followers(model);
            chestBindInverse = Quaternion.Inverse(Chest.rotation) * pose.rotation;
            shoulderWidth = Vector3.Distance(pose.InverseTransformPoint(leftUpperArm.position), pose.InverseTransformPoint(rightUpperArm.position));
            // A forearm roll bone only counts when a skin is bound to it; otherwise the lower arm itself must roll.
            var skinned = new System.Collections.Generic.HashSet<Transform>();
            foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) skinned.UnionWith(skin.bones);
            rightArm = BindArm(false, rightUpperArm, rightLowerArm, rightHand, gripSocket, skinned);
            leftArm = BindArm(true, leftUpperArm, leftLowerArm, leftHand, supportSocket, skinned);

            hasDeath = profile.death != null;
            hasAim = profile.aim != null; hasReload = profile.reload != null; hasHit = profile.hit != null;
            hasMelee = profile.meleeAttack != null;
            sets = profile.weaponSets ?? new WeaponClipSet[0];
            carryClips = new AnimationClipPlayable[sets.Length * CarrySlots];
            if (sets.Length > 0)
            {
                // Valve's xdR rig keeps weapon_bone under Spine, while the authored game rig
                // uses Weapon under the chest. Search the complete pose and accept both names.
                weaponBone = Named(pose, WeaponClipSet.WeaponBone);
                rightTarget = weaponBone != null ? Named(weaponBone, WeaponClipSet.RightHandBone) : null;
                leftTarget = rightTarget != null ? Named(rightTarget, WeaponClipSet.LeftHandBone) : null;
            }
            Reset();
        }

        private void CreateGraph()
        {
            graph = PlayableGraph.Create(animator.name + "_SkinnedCharacter");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(graph, "Pose", animator);

            AnimationClip forward = profile.runForward != null ? profile.runForward : profile.idle;
            AddLoco(Loco.Idle, profile.idle, 0f);
            AddLoco(Loco.Forward, forward, profile.runForwardSpeed);
            AddLoco(Loco.Backward, profile.runBackward != null ? profile.runBackward : forward, profile.runBackward != null ? profile.runBackwardSpeed : profile.runForwardSpeed);
            AddLoco(Loco.Left, profile.strafeLeft != null ? profile.strafeLeft : forward, profile.strafeLeft != null ? profile.strafeLeftSpeed : profile.runForwardSpeed);
            AddLoco(Loco.Right, profile.strafeRight != null ? profile.strafeRight : forward, profile.strafeRight != null ? profile.strafeRightSpeed : profile.runForwardSpeed);
            AddCrouch(Loco.Idle, profile.crouchIdle, 0f);
            AddCrouch(Loco.Forward, profile.crouchForward, profile.crouchForwardSpeed);
            AddCrouch(Loco.Backward, profile.crouchBackward, profile.crouchBackwardSpeed);
            AddCrouch(Loco.Left, profile.crouchLeft, profile.crouchLeftSpeed);
            AddCrouch(Loco.Right, profile.crouchRight, profile.crouchRightSpeed);
            locoMixer = AnimationMixerPlayable.Create(graph, loco.Length);
            for (int i = 0; i < loco.Length; i++) graph.Connect(loco[i], 0, locoMixer, i);

            deathPlayable = AnimationClipPlayable.Create(graph, hasDeath ? profile.death : profile.idle);
            var chargeClips = new[] { profile.chargeTelegraph, profile.chargeDash, profile.chargeRecovery };
            chargeMixer = AnimationMixerPlayable.Create(graph, charge.Length);
            for (int i = 0; i < charge.Length; i++)
            {
                hasCharge[i] = chargeClips[i] != null;
                charge[i] = AnimationClipPlayable.Create(graph, hasCharge[i] ? chargeClips[i] : profile.idle);
                graph.Connect(charge[i], 0, chargeMixer, i);
            }
            bodyMixer = AnimationMixerPlayable.Create(graph, 3);
            graph.Connect(locoMixer, 0, bodyMixer, 0);
            graph.Connect(deathPlayable, 0, bodyMixer, 1);
            graph.Connect(chargeMixer, 0, bodyMixer, 2);

            aimPlayable = AnimationClipPlayable.Create(graph, hasAim ? profile.aim : profile.idle);
            reloadPlayable = AnimationClipPlayable.Create(graph, hasReload ? profile.reload : profile.idle);
            hitPlayable = AnimationClipPlayable.Create(graph, hasHit ? profile.hit : profile.idle);
            meleePlayable = AnimationClipPlayable.Create(graph, hasMelee ? profile.meleeAttack : profile.idle);
            upperMixer = AnimationMixerPlayable.Create(graph, sets.Length > 0 ? 5 : 4);
            graph.Connect(aimPlayable, 0, upperMixer, 0);
            graph.Connect(reloadPlayable, 0, upperMixer, 1);
            graph.Connect(hitPlayable, 0, upperMixer, 2);
            graph.Connect(meleePlayable, 0, upperMixer, 3);
            if (sets.Length > 0)
            {
                carryMixer = AnimationMixerPlayable.Create(graph, carryClips.Length);
                for (int i = 0; i < carryClips.Length; i++)
                {
                    carryClips[i] = AnimationClipPlayable.Create(graph, CarryClip(i / CarrySlots, i % CarrySlots) ?? profile.idle);
                    Freeze(carryClips[i]);
                    graph.Connect(carryClips[i], 0, carryMixer, i);
                }
                graph.Connect(carryMixer, 0, upperMixer, 4);
            }

            layers = AnimationLayerMixerPlayable.Create(graph, sets.Length > 0 ? 3 : 2);
            graph.Connect(bodyMixer, 0, layers, 0);
            graph.Connect(upperMixer, 0, layers, 1);
            layers.SetInputWeight(0, 1f);
            if (profile.upperBodyMask == null) ownedMask = DefaultUpperBodyMask();
            layers.SetLayerMaskFromAvatarMask(1, profile.upperBodyMask != null ? profile.upperBodyMask : ownedMask);
            if (sets.Length > 0)
            {
                // A carried weapon's flinch: the humanoid hit clip has no weapon_bone curves, so mixed with the carry
                // clips it would pull the gun toward its bind pose. On its own layer, masked off the weapon bones, it
                // takes only the body and the carry keeps the gun.
                carriedHitPlayable = AnimationClipPlayable.Create(graph, hasHit ? profile.hit : profile.idle);
                Freeze(carriedHitPlayable);
                graph.Connect(carriedHitPlayable, 0, layers, 2);
                hitMask = WeaponFreeMask(profile.upperBodyMask != null ? profile.upperBodyMask : ownedMask);
                layers.SetLayerMaskFromAvatarMask(2, hitMask);
            }
            output.SetSourcePlayable(layers);
            foreach (var p in new[] { deathPlayable, aimPlayable, reloadPlayable, hitPlayable, meleePlayable }) Freeze(p);
            foreach (var p in charge) Freeze(p);
        }

        private Transform Bone(HumanBodyBones bone) => animator.GetBoneTransform(bone);

        private static Follower[] Followers(Transform model)
        {
            var byName = new System.Collections.Generic.Dictionary<string, Transform>();
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) byName[t.name] = t;
            var list = new System.Collections.Generic.List<Follower>();
            foreach (var t in byName.Values)
                if (HandGripData.Leader(t.name) is string name && byName.TryGetValue(name, out var leader))
                    list.Add(new Follower { bone = t, leader = leader, bind = t.localRotation, leaderUnbind = Quaternion.Inverse(leader.localRotation) });
            return list.ToArray();
        }

        /// <summary>Each ring and little finger bone takes its leader's turn from bind, as the clip posed the leader (before
        /// any grip data replaces both).</summary>
        private void FollowFingers()
        {
            foreach (var f in followers) f.bone.localRotation = f.bind * f.leaderUnbind * f.leader.localRotation;
        }

        private static Transform Named(Transform parent, string name)
        {
            if (parent == null) return null;
            string[] aliases = name == WeaponClipSet.WeaponBone
                ? new[] { name, "ValveBiped.weapon_bone" }
                : name == WeaponClipSet.RightHandBone
                    ? new[] { name, "ValveBiped.weapon_bone_RHand" }
                    : name == WeaponClipSet.LeftHandBone
                        ? new[] { name, "ValveBiped.weapon_bone_LHand" }
                        : new[] { name };
            foreach (var t in parent.GetComponentsInChildren<Transform>(true))
                foreach (var alias in aliases) if (t.name == alias) return t;
            return null;
        }

        private Transform PalmSocket(string name, Transform hand, HumanBodyBones finger)
        {
            var socket = new GameObject(name).transform;
            socket.SetParent(hand, false);
            Transform knuckle = Bone(finger);
            // Between the wrist and the knuckles: where a grip or fore-end actually sits.
            socket.position = knuckle != null ? Vector3.Lerp(hand.position, knuckle.position, .6f) : hand.position;
            return socket;
        }

        /// <summary>Moves a palm socket to the channel of the closed fist (bind pose, fingers open): along the hand
        /// toward the index/ring knuckles, then toward the palm (the thumb side).</summary>
        private void FistSocket(Transform socket, Transform hand, HumanBodyBones index, HumanBodyBones ring, HumanBodyBones thumb)
        {
            Transform i = Bone(index), r = Bone(ring), t = Bone(thumb);
            // ValveBiped hands have no ring finger: the middle finger stands in for the far side of the knuckles.
            if (r == null) r = Bone(ring == HumanBodyBones.RightRingProximal ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
            if (i == null || r == null || t == null) return;
            Vector3 along = ((i.position + r.position) * .5f - hand.position).normalized;
            Vector3 palm = Vector3.Cross(along, i.position - r.position).normalized;
            if (Vector3.Dot(palm, t.position - hand.position) < 0f) palm = -palm;
            float metres = pose.lossyScale.x;
            socket.position = hand.position + (along * profile.fistChannel.x + palm * profile.fistChannel.y) * metres;
        }

        /// <summary>Reads an arm and its fingers at bind (the constructor runs before any clip is applied).</summary>
        private Arm BindArm(bool left, Transform upper, Transform lower, Transform hand, Transform socket,
            System.Collections.Generic.HashSet<Transform> skinned)
        {
            var arm = new Arm
            {
                upper = upper, lower = lower, hand = hand, socket = socket, keep = left ? SupportTwist : GripTwist,
                upperBind = upper.localRotation, lowerBind = lower.localRotation,
                handBind = Quaternion.Inverse(lower.rotation) * hand.rotation,
            };
            int first = (int)(left ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal);
            Transform knuckle = Bone((HumanBodyBones)(first + 6)), thumb = Bone((HumanBodyBones)first);
            // Auto-rigged hands can seat the middle knuckle inside the palm: then the farthest middle joint gives the hand's line.
            float forearm = (hand.position - lower.position).magnitude;
            if (knuckle != null && (knuckle.position - hand.position).magnitude < forearm * .25f)
                knuckle = Bone((HumanBodyBones)(first + 8)) ?? Bone((HumanBodyBones)(first + 7)) ?? knuckle;
            Vector3 a = (knuckle != null ? knuckle.position - hand.position : hand.position - lower.position).normalized;
            // Finger joints are placed unreliably across rigs; the shoulder joints set the hand's size against the hero.
            arm.handScale = shoulderWidth / WeaponHold.HeroShoulderWidth;
            Vector3 t = thumb != null ? thumb.position - hand.position : pose.up;
            Vector3 r = (t - a * Vector3.Dot(t, a)).normalized;
            // Knuckles x thumb leaves a right palm (a left one mirrored).
            Vector3 p = Vector3.Cross(a, r) * (left ? -1f : 1f);
            Quaternion toHand = Quaternion.Inverse(hand.rotation), toForearm = Quaternion.Inverse(lower.rotation);
            arm.handFrameInverse = Quaternion.Inverse(Quaternion.LookRotation(toHand * a, toHand * r));
            arm.twistAxis = (toForearm * (hand.position - lower.position)).normalized;
            arm.flexAxis = Vector3.ProjectOnPlane(toForearm * r, arm.twistAxis).normalized;
            arm.devAxis = Vector3.ProjectOnPlane(toForearm * p, arm.twistAxis).normalized;

            // Fingers flex the knuckles toward the palm; the thumb also swings across it.
            Vector3 flex = Vector3.Cross(a, p), across = Vector3.Cross(r, p);
            var digits = new System.Collections.Generic.List<Digit>(15);
            for (int finger = 0; finger < 5; finger++)
            {
                bool distal = Bone((HumanBodyBones)(first + finger * 3 + 2)) != null;
                for (int joint = 0; joint < 3; joint++)
                {
                    Transform bone = Bone((HumanBodyBones)(first + finger * 3 + joint));
                    if (bone == null) continue;
                    Quaternion inverse = Quaternion.Inverse(bone.rotation);
                    digits.Add(new Digit
                    {
                        bone = bone, bind = bone.localRotation, flex = inverse * flex, across = inverse * across, joint = joint,
                        // A rig with three finger segments shares the hero's two-joint curl between them.
                        share = finger == 0 || !distal || joint == 0 ? 1f : joint == 1 ? .6f : .5f,
                        thumb = finger == 0, index = finger == 1,
                    });
                }
            }
            arm.digits = digits.ToArray();
            foreach (Transform child in lower)
                if ((child.name.EndsWith((left ? "Left" : "Right") + "ForearmTwist", System.StringComparison.Ordinal) ||
                     child.name.EndsWith((left ? "_L" : "_R") + "_Ulna", System.StringComparison.Ordinal)) && skinned.Contains(child))
                {
                    arm.twist = child;
                    arm.twistBind = child.localRotation;
                    arm.twistLong = (Quaternion.Inverse(child.rotation) * (hand.position - lower.position)).normalized;
                }
                else if (hand.parent == lower && child.name.EndsWith((left ? "_L" : "_R") + "_Wrist", System.StringComparison.Ordinal))
                {
                    arm.wrist = child;
                    arm.wristBind = child.localRotation;
                    arm.handLocalBind = hand.localRotation;
                }
            return arm;
        }

        private void AddLoco(Loco slot, AnimationClip clip, float speed, int posture = 0)
        {
            int i = (int)slot + posture * LocoCount;
            loco[i] = AnimationClipPlayable.Create(graph, clip);
            Freeze(loco[i]);
            locoLength[i] = Mathf.Max(clip.length, 1f / 30f);
            postureSpeed[i] = speed;
        }

        /// <summary>The crouched locomotion slot; without its clip it plays the standing one at the standing speed.</summary>
        private void AddCrouch(Loco slot, AnimationClip clip, float speed)
        {
            int i = (int)slot;
            if (clip != null) AddLoco(slot, clip, speed, 1);
            else AddLoco(slot, loco[i].GetAnimationClip(), postureSpeed[i], 1);
        }

        private static void Freeze(AnimationClipPlayable playable)
        {
            // Times are set explicitly every tick; the graph never advances them itself.
            playable.SetSpeed(0);
            playable.SetApplyFootIK(true);
        }

        private static AvatarMask DefaultUpperBodyMask()
        {
            var mask = new AvatarMask { name = "RuntimeUpperBody", hideFlags = HideFlags.DontSave };
            for (var part = AvatarMaskBodyPart.Root; part < AvatarMaskBodyPart.LastBodyPart; part++)
                mask.SetHumanoidBodyPartActive(part,
                    part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.Head ||
                    part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm ||
                    part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers ||
                    part == AvatarMaskBodyPart.LeftHandIK || part == AvatarMaskBodyPart.RightHandIK);
            return mask;
        }

        /// <summary><paramref name="upper"/>'s body parts over every transform of the rig except the weapon bones.</summary>
        private AvatarMask WeaponFreeMask(AvatarMask upper)
        {
            var mask = new AvatarMask { name = "RuntimeCarriedHit", hideFlags = HideFlags.DontSave };
            for (var part = AvatarMaskBodyPart.Root; part < AvatarMaskBodyPart.LastBodyPart; part++)
                mask.SetHumanoidBodyPartActive(part, upper.GetHumanoidBodyPartActive(part));
            mask.AddTransformPath(animator.transform, true);
            for (int i = 0; i < mask.transformCount; i++)
                mask.SetTransformActive(i, !mask.GetTransformPath(i).Contains("weapon_bone"));
            return mask;
        }

        public void Reset()
        {
            phase = 0; idleTime = 0; legYaw = 0; reloadWeight = 0; hitTime = -1f; lastHit = 0;
            dying = false; deathTime = 0; deathWeight = 0;
            meleeWeight = chargeWeight = 0;
            System.Array.Clear(chargeWeights, 0, chargeWeights.Length);
            anchorKnown = gripKnown = false;
            directionKnown = false;
            fireTime = -1f; fireWeight = 0f; drawDip = carryMoving = 0f;
        }

        /// <param name="localVelocity">Smoothed planar velocity in the pose frame (m/s).</param>
        /// <param name="moving">0..1 idle-to-locomotion blend.</param>
        /// <param name="armed">A weapon is in the hands.</param>
        /// <param name="aim">0..1 shouldered aim; 0 is the low-ready. Clip-led guard holds always show their aim clip.</param>
        /// <param name="action">Clip and time the caller derived from gameplay this tick; None fades actions out.</param>
        public void Advance(float dt, Vector3 localVelocity, float moving, float reload, float hit, float death, bool armed,
            float aim, CharacterAction action = default)
        {
            // Unselected variants live under inactive pool actors and may never receive OnDestroy.
            if (disposed || !animator.gameObject.activeInHierarchy) return;
            if (!graph.IsValid()) CreateGraph();
            // ---- death: starts on the rising edge, plays once, holds; cleared by death == 0
            if (death > 0f && !dying) { dying = true; deathTime = 0f; }
            else if (death <= 0f && dying) Reset();
            if (dying)
            {
                deathTime += dt;
                deathWeight = Mathf.MoveTowards(deathWeight, hasDeath ? 1f : 0f, dt / .12f);
            }

            // ---- locomotion: one shared phase; every loop starts at left-foot touchdown
            for (int i = 0; i < LocoCount; i++) locoSpeed[i] = Mathf.Lerp(postureSpeed[i], postureSpeed[i + LocoCount], crouch);
            Vector2 v = new Vector2(localVelocity.x, localVelocity.z);
            float speed = v.magnitude;
            float targetYaw = speed > .3f && !dying ? ChooseLegYaw(v, speed) : 0f;
            legYaw = Mathf.LerpAngle(legYaw, targetYaw, 1f - Mathf.Exp(-9f * dt));
            Vector2 legDir = speed > .01f ? Rotate(v / speed, -legYaw) : Vector2.up;
            var w = weights;
            DirectionWeights(legDir, targetWeights);
            float difference = 0f;
            for (int i = 1; i < 5; i++) difference = Mathf.Max(difference, Mathf.Abs(targetWeights[i] - w[i]));
            // Bound weight travel after normalising velocity: crossing zero must not swap clips in one tick.
            float blend = directionKnown && moving > .001f && difference > 0f ? Mathf.Min(1f, dt / (DirectionBlendTime * difference)) : 1f;
            float capacity = 0f;
            for (int i = 1; i < 5; i++)
            {
                w[i] = Mathf.Lerp(w[i], targetWeights[i], blend);
                capacity += w[i] * locoSpeed[i];
            }
            directionKnown |= speed > .01f;
            float rate = capacity > .01f ? Mathf.Clamp(speed / capacity, .5f, Mathf.Max(1f, profile.maxPlaybackRate)) : 1f;
            float cycle = 0f;
            for (int i = 1; i < 5; i++) cycle += w[i] * Mathf.Lerp(locoLength[i], locoLength[i + LocoCount], crouch);
            if (moving > .001f && cycle > .01f) phase = Mathf.Repeat(phase + dt * rate / cycle, 1f);
            idleTime += dt;
            for (int posture = 0; posture < Postures; posture++)
            {
                int o = posture * LocoCount;
                float share = PostureShare(posture);
                locoMixer.SetInputWeight(o + (int)Loco.Idle, (1f - moving) * share);
                loco[o + (int)Loco.Idle].SetTime(Mathf.Repeat(idleTime, locoLength[o]));
                for (int i = 1; i < 5; i++)
                {
                    locoMixer.SetInputWeight(o + i, w[i] * moving * share);
                    loco[o + i].SetTime(phase * locoLength[o + i]);
                }
            }
            // ---- charge actions: full body, faded over the locomotion; death overrides them
            float fade = dt / ActionFadeTime;
            chargeWeight = 0f;
            for (int i = 0; i < charge.Length; i++)
            {
                bool on = !dying && hasCharge[i] && (int)action.kind == (int)CharacterActionKind.ChargeTelegraph + i;
                chargeWeights[i] = Mathf.MoveTowards(chargeWeights[i], on ? 1f : 0f, fade);
                if (on) charge[i].SetTime(ClipTime(charge[i], action.time, i == 1));
                chargeWeight += chargeWeights[i];
            }
            if (chargeWeight > 1f)
                for (int i = 0; i < charge.Length; i++) chargeWeights[i] /= chargeWeight;
            chargeWeight = Mathf.Min(1f, chargeWeight);
            for (int i = 0; i < charge.Length; i++) chargeMixer.SetInputWeight(i, chargeWeight > 0f ? chargeWeights[i] / chargeWeight : 0f);
            bodyMixer.SetInputWeight(0, (1f - deathWeight) * (1f - chargeWeight));
            bodyMixer.SetInputWeight(1, deathWeight);
            bodyMixer.SetInputWeight(2, (1f - deathWeight) * chargeWeight);
            if (hasDeath) deathPlayable.SetTime(Mathf.Min(deathTime, deathPlayable.GetAnimationClip().length));

            // ---- upper body: aim hold, gameplay-timed reload, one-shot hit
            int c = carrySet * CarrySlots;
            bool reloading = reload >= 0f && (carrySet >= 0 ? CarryClip(carrySet, ReloadSlot) != null : hasReload) && armed && !dying;
            reloadWeight = Mathf.MoveTowards(reloadWeight, reloading ? 1f : 0f, dt / .08f);
            if (reloading && carrySet >= 0) CarryReload(c, Mathf.Clamp01(reload));
            else if (reloading) reloadPlayable.SetTime(Mathf.Clamp01(reload) * profile.reload.length);
            if (carrySet >= 0)
            {
                // The fire clip plays once from each shot and fades out over its last frames.
                AnimationClip fire = sets[carrySet].fire;
                if (fireTime >= 0f) fireTime += dt;
                fireWeight = fire != null && fireTime >= 0f && fireTime < fire.length ? Mathf.Clamp01((fire.length - fireTime) / ActionFadeTime) : 0f;
                for (int s = c; s < c + CarrySlots; s += PostureSlots)
                {
                    carryClips[s + HoldSlot].SetTime(ClipTime(carryClips[s + HoldSlot], idleTime, true));
                    carryClips[s + AimSlot].SetTime(ClipTime(carryClips[s + AimSlot], idleTime, true));
                    // The moving carry keeps the gait's phase.
                    carryClips[s + RunSlot].SetTime(phase * carryClips[s + RunSlot].GetAnimationClip().length);
                    if (fireWeight > 0f) carryClips[s + FireSlot].SetTime(fireTime);
                }
                carryMoving = moving;
                // A draw dips the weapon quickly, keeps it low, then raises it to the ready by the end.
                bool drawing = drawProgress >= 0f && drawProgress < 1f && !dying;
                float dip = drawing ? Mathf.SmoothStep(0f, 1f, drawProgress / DrawDipIn)
                    * (1f - Mathf.SmoothStep(0f, 1f, (drawProgress - DrawDipOut) / (1f - DrawDipOut))) : 0f;
                drawDip = Mathf.MoveTowards(drawDip, dip, dt / ActionFadeTime);
            }
            if (hit > lastHit + .01f && hasHit) hitTime = 0f;
            lastHit = hit;
            if (hitTime >= 0f) hitTime += dt;
            float hitLength = hasHit ? profile.hit.length : 0f;
            bool hitting = hasHit && hitTime >= 0f && hitTime < hitLength && !reloading;
            if (hitTime >= hitLength) hitTime = -1f;
            // A swing in progress owns the arms; a hit landing during it only flashes.
            bool swinging = !dying && hasMelee && action.kind == CharacterActionKind.Melee;
            meleeWeight = Mathf.MoveTowards(meleeWeight, swinging ? 1f : 0f, fade);
            if (swinging) meleePlayable.SetTime(ClipTime(meleePlayable, action.time, false));
            hitWeight = hitting ? 1f - meleeWeight : 0f;
            if (hitting)
            {
                hitPlayable.SetTime(hitTime);
                if (carriedHitPlayable.IsValid()) carriedHitPlayable.SetTime(hitTime);
            }
            reloadShare = reloadWeight * (1f - hitWeight) * (1f - meleeWeight);
            aimShare = !armed ? 0f : profile.guardHold ? 1f : Mathf.Clamp01(aim);
            // A forward run that carries its own upper body shows its lean and carry through the guard.
            forwardRun = profile.runCarriesUpperBody ? moving * w[(int)Loco.Forward] : 0f;
            SetUpperWeights();
        }

        /// <summary>A shot shoulders the weapon at once, before the next tick.</summary>
        public void RaiseAim()
        {
            if (!graph.IsValid() || profile.guardHold || carrySet >= 0) return;
            aimShare = 1f;
            SetUpperWeights();
        }

        private void SetUpperWeights()
        {
            if (carrySet >= 0)
            {
                SetCarryWeights();
                return;
            }
            // Reload, hit and melee clips take the arms from the aim; the weapon then rides the low-ready.
            holdAim = aimShare * Mathf.Max(0f, 1f - reloadShare - hitWeight - meleeWeight);
            float aimWeight = hasAim ? holdAim * (1f - forwardRun) : 0f;
            upperMixer.SetInputWeight(0, aimWeight);
            upperMixer.SetInputWeight(1, reloadShare);
            upperMixer.SetInputWeight(2, hitWeight);
            upperMixer.SetInputWeight(3, meleeWeight);
            if (sets.Length > 0)
            {
                upperMixer.SetInputWeight(4, 0f);
                layers.SetInputWeight(2, 0f);
            }
            float upper = Mathf.Min(1f, aimWeight + reloadShare + hitWeight + meleeWeight);
            // Charge clips carry their own arms and weapon line.
            layers.SetInputWeight(1, upper * (1f - deathWeight) * (1f - chargeWeight));
        }

        /// <summary>
        /// The carried weapon's clips own the arms: hold, with aim over it standing and the run carry moving, a shot's
        /// fire clip over those and the reload over all; hit and melee clips take the arms from them.
        /// </summary>
        private void SetCarryWeights()
        {
            // The generic hit flinch takes only part of the arms, so the left hand still reaches the fore-end.
            float hit = hitWeight * CarriedHitShare;
            float carried = Mathf.Max(0f, 1f - meleeWeight);
            float ready = 1f - reloadShare, fire = fireWeight * ready, stand = (ready - fire) * (1f - carryMoving);
            // The chest and head take the aim as far as the weapon is brought to the sights: from the hip they keep the clip's.
            holdAim = carried * ready * (1f - drawDip) * aimShare;
            int c = carrySet * CarrySlots;
            for (int i = 0; i < carryClips.Length; i++) carryMixer.SetInputWeight(i, 0f);
            for (int posture = 0; posture < Postures; posture++)
            {
                int s = c + posture * PostureSlots;
                float share = PostureShare(posture);
                carryMixer.SetInputWeight(s + HoldSlot, share * stand * (1f - aimShare));
                carryMixer.SetInputWeight(s + AimSlot, share * stand * aimShare);
                carryMixer.SetInputWeight(s + RunSlot, share * (ready - fire) * carryMoving);
                carryMixer.SetInputWeight(s + FireSlot, share * fire);
                for (int part = 0; part < reloadParts.Length; part++) carryMixer.SetInputWeight(s + ReloadSlot + part, share * reloadShare * reloadParts[part]);
            }
            upperMixer.SetInputWeight(0, 0f);
            upperMixer.SetInputWeight(1, 0f);
            upperMixer.SetInputWeight(2, 0f);
            upperMixer.SetInputWeight(3, meleeWeight);
            upperMixer.SetInputWeight(4, carried);
            layers.SetInputWeight(1, (1f - deathWeight) * (1f - chargeWeight));
            layers.SetInputWeight(2, hit * (1f - deathWeight) * (1f - chargeWeight));
        }

        /// <summary>
        /// Poses the carried reload at <paramref name="progress"/>. A reload by the round with a shell-by-shell set plays the
        /// start fitted to the game's start phase, the insert looping at its own speed, then crossfades into the end fitted to
        /// the end phase; otherwise the reload clip is fitted to the whole reload.
        /// </summary>
        private void CarryReload(int c, float progress)
        {
            var set = sets[carrySet];
            System.Array.Clear(reloadParts, 0, reloadParts.Length);
            float start = reloadPhases.x, end = reloadPhases.y;
            if (!set.ShellReload || reloadSeconds <= 0f || start <= 0f || end <= 0f || start + end > reloadSeconds)
            {
                reloadParts[0] = 1f;
                FitReload(c, ReloadSlot, progress);
                return;
            }
            float time = progress * reloadSeconds, ending = reloadSeconds - end;
            if (time < start)
            {
                reloadParts[1] = 1f;
                FitReload(c, ReloadStartSlot, time / start);
                return;
            }
            float toEnd = Mathf.Clamp01((time - ending) / ShellEndBlend);
            reloadParts[2] = 1f - toEnd;
            reloadParts[3] = toEnd;
            for (int s = c; s < c + CarrySlots; s += PostureSlots)
                carryClips[s + ReloadInsertSlot].SetTime(ClipTime(carryClips[s + ReloadInsertSlot], time - start, true));
            FitReload(c, ReloadEndSlot, Mathf.Clamp01((time - ending) / end));
        }

        /// <summary>Both postures' clips in reload slot <paramref name="slot"/> at <paramref name="fraction"/> of their length.</summary>
        private void FitReload(int c, int slot, float fraction)
        {
            for (int s = c + slot; s < c + CarrySlots; s += PostureSlots) carryClips[s].SetTime(fraction * carryClips[s].GetAnimationClip().length);
        }

        /// <summary>Share of the pose posture 0 (standing) or 1 (crouched) takes.</summary>
        private float PostureShare(int posture) => posture == 0 ? 1f - crouch : crouch;

        /// <summary>A set's clip by slot; a missing hold, aim or run falls back to the others, then to the profile's aim clip, and a
        /// missing reload to the profile's reload. A missing fire clip stays missing (the kick alone). A missing crouched clip
        /// is its standing one.</summary>
        private AnimationClip CarryClip(int set, int slot)
        {
            var s = sets[set];
            if (slot >= PostureSlots)
            {
                AnimationClip crouched;
                switch (slot - PostureSlots)
                {
                    case HoldSlot: crouched = s.crouchHold; break;
                    case AimSlot: crouched = s.crouchAim; break;
                    case RunSlot: crouched = s.crouchRun; break;
                    case FireSlot: crouched = s.crouchFire; break;
                    case ReloadStartSlot: crouched = s.crouchReloadStart; break;
                    case ReloadInsertSlot: crouched = s.crouchReloadInsert; break;
                    case ReloadEndSlot: crouched = s.crouchReloadEnd; break;
                    default: crouched = s.crouchReload; break;
                }
                return crouched != null ? crouched : CarryClip(set, slot - PostureSlots);
            }
            AnimationClip hold = s.hold != null ? s.hold : s.aim != null ? s.aim : profile.aim;
            switch (slot)
            {
                case HoldSlot: return hold;
                case AimSlot: return s.aim != null ? s.aim : hold;
                case RunSlot: return s.run != null ? s.run : hold;
                case FireSlot: return s.fire;
                case ReloadStartSlot: return s.reloadStart;
                case ReloadInsertSlot: return s.reloadInsert;
                case ReloadEndSlot: return s.reloadEnd;
                default: return s.reload != null ? s.reload : profile.reload;
            }
        }

        private static float ClipTime(AnimationClipPlayable playable, float time, bool loop)
        {
            float length = Mathf.Max(playable.GetAnimationClip().length, 1f / 30f);
            return loop ? Mathf.Repeat(time, length) : Mathf.Clamp(time, 0f, length);
        }

        /// <summary>Writes the clip pose (no time advance) and every in-place correction.</summary>
        /// <param name="hold">Fitted hold for the weapon; null (or a guard-holding profile) keeps the clip-led hold.</param>
        /// <param name="holdScale">Weapon root units per native model unit.</param>
        /// <param name="aimPoint">World point the shouldered bore meets (the crosshair); null aims level.</param>
        public void Apply(Transform weaponRoot, Transform weaponGrip, Transform weaponSupport, Transform weaponMuzzle, WeaponHold hold, float holdScale,
            float recoil, float reload, Vector3? aimPoint, bool armed)
        {
            if (!graph.IsValid()) return;
            graph.Evaluate(0f);
            if (dying)
            {
                // The clip is final (the caller hides the weapon); only keep the lying body on the floor.
                var lift = profile.deathFloorLift;
                if (lift != null && lift.length > 0)
                    hips.position += pose.up * (lift.Evaluate(deathTime) * deathWeight * pose.lossyScale.y);
                DriveTwist(leftArm);
                DriveTwist(rightArm);
                DriveWrist(leftArm);
                DriveWrist(rightArm);
                FollowFingers();
                PlaceShell(null);
                return;
            }
            CarryThroughHit();
            FollowFingers();
            // A carried weapon rides the chest as the clips place it (the bake keeps it there), though it hangs under the lowest
            // spine bone: the leg-yaw twist below turns that bone by only its share.
            Transform torso = Chest;
            bool onChest = armed && weaponRoot != null && carrySet >= 0 && weaponBone != null;
            Vector3 weaponOnChest = onChest ? torso.InverseTransformPoint(weaponBone.position) : default;
            Quaternion weaponTurnOnChest = onChest ? Quaternion.Inverse(torso.rotation) * weaponBone.rotation : default;

            Vector3 up = pose.up;
            float action = ActionWeight;
            // Legs face the travel the clips can carry; the spine returns the chest to the facing.
            // Charge clips are authored facing their travel and keep their own hips.
            float yaw = legYaw * (1f - chargeWeight);
            if (Mathf.Abs(yaw) > .01f) hips.rotation = Quaternion.AngleAxis(yaw, up) * hips.rotation;
            TwistSpine(Mathf.Clamp(-yaw, -MaxTorsoTwist, MaxTorsoTwist), up);

            if (armed && weaponRoot != null)
            {
                if (carrySet >= 0)
                {
                    // The carry clips are baked on hips square to the facing: the spine's root takes back, whole, the turn the
                    // profile's own clips give the hips (the leg yaw is already taken back above), so every spine bone and the
                    // Weapon bone under the lowest one sit as baked.
                    Vector3 hipsForward = Vector3.Cross(rightUpperLeg.position - leftUpperLeg.position, up);
                    float square = yaw - Vector3.SignedAngle(Vector3.ProjectOnPlane(pose.forward, up), Vector3.ProjectOnPlane(hipsForward, up), up);
                    if (spineChain.Length > 0) spineChain[0].rotation = Quaternion.AngleAxis(square, up) * spineChain[0].rotation;
                    if (onChest) weaponBone.SetPositionAndRotation(torso.TransformPoint(weaponOnChest), torso.rotation * weaponTurnOnChest);
                    // The whole spine takes the aim, so the arms, the weapon and the cheek on its stock move as one.
                    Pitch(spineChain, AimPitch(aimPoint) * holdAim);
                    AimCarried(weaponRoot, weaponMuzzle, aimPoint);
                    Pitch(spineChain, recoil * RecoilChest);
                    CarryWeapon(weaponRoot, weaponGrip, weaponSupport, recoil);
                }
                else if (hold != null && weaponMuzzle != null && !profile.guardHold)
                {
                    float pitch = AimPitch(aimPoint);
                    // The chest follows part of the aim pitch and rocks back with the shot.
                    Pitch(spineChain, pitch * ChestAimShare * holdAim + recoil * RecoilChest);
                    Pitch(headChain, pitch * (1f - ChestAimShare) * holdAim);
                    HoldWeapon(weaponRoot, hold, holdScale, weaponRoot.InverseTransformPoint(weaponMuzzle.position), recoil, reload, pitch, aimPoint, action);
                }
                else ClipHold(weaponRoot, weaponGrip, weaponSupport, recoil, reload, profile.guardHold ? 1f : action);
            }
            DriveTwist(leftArm);
            DriveTwist(rightArm);
            DriveWrist(leftArm);
            DriveWrist(rightArm);
            GroundFoot(leftUpperLeg, leftLowerLeg, leftFoot, leftToes, leftSole);
            GroundFoot(rightUpperLeg, rightLowerLeg, rightFoot, rightToes, rightSole);
            PlaceShell(armed && weaponRoot != null && carrySet >= 0 && weaponBone != null ? sets[carrySet].grip : null);
        }

        /// <summary>
        /// The carried hit flinch leaves the weapon bone out (the generic clip has no weapon track) and the weapon bone hangs
        /// below the chest, so the flinch would turn the shoulders away from a still weapon and drag the hands off their wrists.
        /// The weapon takes the chest's flinch instead: the pose is evaluated once without the flinch to read where the
        /// chest and weapon were, and the weapon keeps that place on the flinching chest.
        /// </summary>
        private void CarryThroughHit()
        {
            if (carrySet < 0 || weaponBone == null || layers.GetInputCount() < 3) return;
            float weight = layers.GetInputWeight(2);
            Transform torso = upperChest != null ? upperChest : chest;
            if (weight <= 0f || torso == null) return;
            Vector3 hitPosition = torso.position;
            Quaternion hitRotation = torso.rotation;
            layers.SetInputWeight(2, 0f);
            graph.Evaluate(0f);
            Vector3 position = torso.position, weaponPosition = weaponBone.position;
            Quaternion rotation = torso.rotation, weaponRotation = weaponBone.rotation;
            layers.SetInputWeight(2, weight);
            graph.Evaluate(0f);
            Quaternion turn = hitRotation * Quaternion.Inverse(rotation);
            weaponBone.SetPositionAndRotation(hitPosition + turn * (weaponPosition - position), turn * weaponRotation);
        }

        /// <summary>The forearm roll bone takes the hand's twist off the lower arm beyond the wrist's share, whatever posed the hand.</summary>
        private static void DriveTwist(Arm arm)
        {
            if (arm.twist == null) return;
            Quaternion d = Quaternion.Inverse(arm.lower.rotation) * arm.hand.rotation * Quaternion.Inverse(arm.handBind);
            arm.twist.localRotation = arm.twistBind * Quaternion.AngleAxis(Roll(arm, Twist(d, arm.twistAxis)), arm.twistLong);
        }

        /// <summary>
        /// Valve's procedural wrist helper turns halfway from the distal forearm (the lower arm and its roll bone) to the hand,
        /// so a wrist band skinned to it keeps its volume when the hand bends or twists instead of pinching between two bones.
        /// </summary>
        private static void DriveWrist(Arm arm)
        {
            if (arm.wrist == null) return;
            Quaternion bend = arm.hand.localRotation * Quaternion.Inverse(arm.handLocalBind);
            Quaternion roll = arm.twist != null ? arm.twist.localRotation * Quaternion.Inverse(arm.twistBind) : Quaternion.identity;
            arm.wrist.localRotation = roll * Quaternion.Slerp(Quaternion.identity, Quaternion.Inverse(roll) * bend, .5f) * arm.wristBind;
        }

        private void TwistSpine(float degrees, Vector3 up)
        {
            if (Mathf.Abs(degrees) < .01f || spineChain.Length == 0) return;
            Quaternion step = Quaternion.AngleAxis(degrees / spineChain.Length, up);
            foreach (var bone in spineChain) bone.rotation = step * bone.rotation;
        }

        /// <summary>Pitches a bone chain, spread over its bones; positive degrees lift it (and lean it back).</summary>
        private void Pitch(Transform[] bones, float degrees)
        {
            if (Mathf.Abs(degrees) < .01f || bones.Length == 0) return;
            Quaternion step = Quaternion.AngleAxis(-degrees / bones.Length, pose.right);
            foreach (var bone in bones) bone.rotation = step * bone.rotation;
        }

        /// <summary>Elevation of the aim point from the right shoulder, degrees, inside the shouldered range.</summary>
        private float AimPitch(Vector3? aimPoint)
        {
            if (!aimPoint.HasValue) return 0f;
            Vector3 d = aimPoint.Value - rightUpperArm.position;
            float flat = Vector3.ProjectOnPlane(d, pose.up).magnitude;
            if (flat < .5f) return 0f;
            return Mathf.Clamp(Mathf.Atan2(Vector3.Dot(d, pose.up), flat) * Mathf.Rad2Deg, -MaxAimDrop, MaxAimRise);
        }

        /// <summary>Turns the spine (at most <see cref="MaxBoreTurn"/> each way) until the carried bore meets the crosshair point.</summary>
        private void AimCarried(Transform root, Transform muzzle, Vector3? aimPoint)
        {
            if (!aimPoint.HasValue || muzzle == null || holdAim <= 0f) return;
            Vector3 up = pose.up;
            float yawTotal = 0f, riseTotal = 0f;
            for (int pass = 0; pass < 2; pass++)
            {
                root.SetPositionAndRotation(weaponBone.position, weaponBone.rotation);
                Vector3 bore = muzzle.forward, wanted = aimPoint.Value - muzzle.position;
                if (wanted.sqrMagnitude < 1f) return;
                float yaw = Vector3.SignedAngle(Vector3.ProjectOnPlane(bore, up), Vector3.ProjectOnPlane(wanted, up), up) * holdAim;
                float rise = (Elevation(wanted, up) - Elevation(bore, up)) * holdAim;
                yaw = Mathf.Clamp(yawTotal + yaw, -MaxBoreTurn, MaxBoreTurn) - yawTotal;
                rise = Mathf.Clamp(riseTotal + rise, -MaxBoreTurn, MaxBoreTurn) - riseTotal;
                yawTotal += yaw; riseTotal += rise;
                TwistSpine(yaw, up);
                Pitch(spineChain, rise);
            }
        }

        private static float Elevation(Vector3 v, Vector3 up) => Mathf.Atan2(Vector3.Dot(v, up), Vector3.ProjectOnPlane(v, up).magnitude) * Mathf.Rad2Deg;

        /// <summary>
        /// The weapon rides the Weapon bone (its root takes the bone's frame): a draw dips it about the right shoulder, a shot
        /// kicks it back and lifts the muzzle about the right wrist. The wrist targets under the bone ride along (written to
        /// <paramref name="grip"/> and <paramref name="support"/>) and both arms are solved onto them with the wrists kept in
        /// range. The bones themselves stay as the clips posed them.
        /// </summary>
        private void CarryWeapon(Transform root, Transform grip, Transform support, float recoil)
        {
            Vector3 position = weaponBone.position;
            Quaternion rotation = weaponBone.rotation;
            void Turn(Quaternion turn, Vector3 pivot)
            {
                position = pivot + turn * (position - pivot);
                rotation = turn * rotation;
            }
            Vector3 Carried(Vector3 point) => position + rotation * (Quaternion.Inverse(weaponBone.rotation) * (point - weaponBone.position));
            if (drawDip > 0f) Turn(Quaternion.AngleAxis(DrawDip * drawDip, pose.right), rightUpperArm.position);
            if (recoil > .001f)
            {
                // The world model's bore is its +Z.
                Vector3 bore = rotation * Vector3.forward, lift = Vector3.Cross(bore, pose.up);
                Turn(Quaternion.AngleAxis(recoil * RecoilRise, lift), Carried(rightTarget.position));
                position -= bore * (recoil * RecoilKick * pose.lossyScale.x);
            }
            root.SetPositionAndRotation(position, rotation);
            var data = sets[carrySet].grip;
            if (data != null)
            {
                GripHand(data, false, position, rotation, grip);
                GripHand(data, true, position, rotation, support);
                PlaceMagazine(data, root);
                return;
            }
            Quaternion moved = rotation * Quaternion.Inverse(weaponBone.rotation);
            Vector3 rightWrist = Carried(rightTarget.position), leftWrist = Carried(leftTarget.position);
            Quaternion rightRotation = moved * rightTarget.rotation, leftRotation = moved * leftTarget.rotation;
            if (grip != null) grip.SetPositionAndRotation(rightWrist, rightRotation);
            if (support != null) support.SetPositionAndRotation(leftWrist, leftRotation);
            SolveWrist(rightArm, rightWrist, rightRotation);
            SolveWrist(leftArm, leftWrist, leftRotation);
        }

        /// <summary>The arm onto a wrist target, keeping the clip's elbow bend; a wrist out of range moves so the palm stays put.</summary>
        private static void SolveWrist(Arm arm, Vector3 wrist, Quaternion rotation)
        {
            Vector3 palm = wrist + rotation * (Quaternion.Inverse(arm.hand.rotation) * (arm.socket.position - arm.hand.position));
            PlaceHand(arm, palm, wrist, rotation, Bend(arm));
        }

        /// <summary>
        /// One hand from the carried set's grip data: the wrist pinned exactly on the weapon frame (<paramref name="position"/>,
        /// <paramref name="rotation"/>) × its grip frame, or × the playing reload clip's track frame (blended by the reload
        /// clips' weights; the static grip keeps the rest), the elbow in the clip's bend plane with bind bone lengths, then
        /// the fingers set to the data's rotations by the same weights. <paramref name="socket"/> takes the wrist frame.
        /// </summary>
        private void GripHand(HandGripData data, bool left, Vector3 position, Quaternion rotation, Transform socket)
        {
            Arm arm = left ? leftArm : rightArm;
            GripPose grip = left ? data.left : data.right;
            int c = carrySet * CarrySlots;
            float tracked = 0f;
            for (int part = 0; part < ReloadTracks; part++)
                if (ReloadTrack(data, c, part, out _, out _) is float w) tracked += w;
            float still = Mathf.Max(0f, 1f - tracked), sum = still;
            Vector3 local = grip.position * still;
            Quaternion turn = grip.rotation;
            for (int part = 0; part < ReloadTracks; part++)
            {
                if (!(ReloadTrack(data, c, part, out var track, out float frame) is float w)) continue;
                var poses = left ? track.left : track.right;
                Sample(poses, frame, out Vector3 p, out Quaternion q);
                local += p * w;
                sum += w;
                turn = Quaternion.Slerp(turn, q, w / sum);
            }
            float scale = weaponBone.lossyScale.x;
            Vector3 wrist = position + rotation * (local * scale);
            Quaternion hand = rotation * turn;
            Vector3 elbowOffset = arm.lower.position - arm.upper.position;
            ShoulderReach(arm, Bone(left ? HumanBodyBones.LeftShoulder : HumanBodyBones.RightShoulder), wrist);
            // The clip's elbow offset from the shoulder; SolveArm keeps its part across the shoulder-wrist line.
            SolveArm(arm, wrist, hand, elbowOffset);
            if (socket != null) socket.SetPositionAndRotation(wrist, hand);

            var fingers = left ? data.leftFingers : data.rightFingers;
            var bones = FingerBones(fingers);
            if (still > 0f)
                for (int i = 0; i < fingers.Length; i++)
                    if (bones[i] != null) bones[i].localRotation = Quaternion.Slerp(bones[i].localRotation, fingers[i].rotation, still);
            // The fire clips' fingers (the gun's trigger pull) on the static grip, by the fire clips' share of it.
            float fired = 0f;
            for (int posture = 0; posture < Postures; posture++)
            {
                var firing = carryClips[c + posture * PostureSlots + FireSlot];
                float fire = still > 0f && firing.IsValid() ? carryMixer.GetInputWeight(c + posture * PostureSlots + FireSlot) : 0f;
                if (fire <= 0f || !(TrackOf(data, c, posture, FireSlot) is GripTrack fireTrack)) continue;
                fired += fire;
                BlendFingers(fireTrack, left, (float)firing.GetTime() * HandGripData.TrackRate, Mathf.Min(1f, fired / still) * fire / fired);
            }
            float blended = still;
            for (int part = 0; part < ReloadTracks; part++)
            {
                if (!(ReloadTrack(data, c, part, out var track, out float frame) is float w)) continue;
                var names = left ? track.leftBones : track.rightBones;
                if (names.Length == 0 || (left ? track.leftFingers : track.rightFingers).Length < names.Length) continue;
                blended += w;
                BlendFingers(track, left, frame, w / blended);
            }
        }

        /// <summary>One side's finger rotations of <paramref name="track"/> at <paramref name="frame"/> (interpolated, clamped),
        /// blended over the current ones by <paramref name="weight"/>.</summary>
        private void BlendFingers(GripTrack track, bool left, float frame, float weight)
        {
            var names = left ? track.leftBones : track.rightBones;
            var rows = left ? track.leftFingers : track.rightFingers;
            if (names.Length == 0 || rows.Length < names.Length) return;
            var bones = FingerBones(names);
            int frames = rows.Length / names.Length;
            float f = Mathf.Clamp(frame, 0f, frames - 1);
            int a = (int)f, b = Mathf.Min(a + 1, frames - 1);
            for (int i = 0; i < names.Length; i++)
            {
                if (bones[i] == null) continue;
                Quaternion held = Quaternion.Slerp(rows[a * names.Length + i], rows[b * names.Length + i], f - a);
                bones[i].localRotation = Quaternion.Slerp(bones[i].localRotation, held, weight);
            }
        }

        /// <summary>
        /// The grip track of the clip in <paramref name="slot"/> of <paramref name="posture"/>; a crouched clip without its own
        /// takes its standing clip's (the CS:GO crouched reloads and shots move the hands on the gun as the standing ones do).
        /// </summary>
        private GripTrack TrackOf(HandGripData data, int c, int posture, int slot) =>
            data.Track(carryClips[c + posture * PostureSlots + slot].GetAnimationClip()) ?? (posture > 0 ? data.Track(carryClips[c + slot].GetAnimationClip()) : null);

        /// <summary>
        /// Weight of reload part <paramref name="part"/> (whole, start, insert, end, standing then crouched) when it plays a clip with a track in
        /// <paramref name="data"/>, with that track and the clip's frame at the track rate; null otherwise.
        /// </summary>
        private float? ReloadTrack(HandGripData data, int c, int part, out GripTrack track, out float frame)
        {
            track = null;
            frame = 0f;
            int posture = part / reloadParts.Length, piece = part % reloadParts.Length;
            float w = reloadShare * reloadParts[piece] * PostureShare(posture);
            if (w <= 0f) return null;
            var playable = carryClips[c + posture * PostureSlots + ReloadSlot + piece];
            track = TrackOf(data, c, posture, ReloadSlot + piece);
            if (track == null) return null;
            frame = (float)playable.GetTime() * HandGripData.TrackRate;
            return w;
        }

        /// <summary>
        /// The magazine cut out by <see cref="MagazineSplit"/> through the playing reload clip's track: from release to insert
        /// its mount takes the track's frame over the seated pose, by the clip's weight; out of sight in the hidden frames;
        /// seated otherwise.
        /// </summary>
        private void PlaceMagazine(HandGripData data, Transform root)
        {
            if (!data.HasMagazine) return;
            if (!magazineMounts.TryGetValue(root, out var mount))
                magazineMounts[root] = mount = System.Array.Find(root.GetComponentsInChildren<Transform>(true), t => t.name == MagazineSplit.MountName);
            if (mount == null) return;
            Vector3 position = Vector3.zero;
            Quaternion rotation = Quaternion.identity;
            bool shown = true;
            float strongest = 0f;
            int c = carrySet * CarrySlots;
            for (int part = 0; part < ReloadTracks; part++)
            {
                if (!(ReloadTrack(data, c, part, out var track, out float frame) is float w) || w <= strongest || track.magazine.Length == 0) continue;
                strongest = w;
                shown = track.magazineHiddenFrom < 0 || frame < track.magazineHiddenFrom || frame >= track.magazineHiddenTo + 1;
                position = Vector3.zero;
                rotation = Quaternion.identity;
                if (frame < track.magazineRelease || frame >= track.magazineInsert) continue;
                Sample(track.magazine, frame - track.magazineRelease, out var p, out var q);
                position = p * w;
                rotation = Quaternion.Slerp(Quaternion.identity, q, w);
            }
            mount.SetLocalPositionAndRotation(position, rotation);
            if (mount.gameObject.activeSelf != shown) mount.gameObject.SetActive(shown);
        }

        /// <summary>
        /// The carried shell (<see cref="HandGripData.HasShell"/>) on the left Hand bone at the data's frame while every playing
        /// reload part's track holds one (<see cref="GripTrack.HoldsShell"/>): a crossfade into a part without it (the loop into
        /// the end) hides it at once rather than carrying it off the port. Hidden otherwise, and for any other gun or state.
        /// </summary>
        private void PlaceShell(HandGripData data)
        {
            bool held = false;
            if (data != null && data.HasShell)
            {
                int c = carrySet * CarrySlots;
                for (int part = 0; part < ReloadTracks; part++)
                {
                    if (!(ReloadTrack(data, c, part, out var track, out float frame) is float)) continue;
                    held = track.HoldsShell(frame);
                    if (!held) break;
                }
            }
            if (!held)
            {
                if (heldShell != null && heldShell.gameObject.activeSelf) heldShell.gameObject.SetActive(false);
                return;
            }
            if (heldShell == null)
            {
                var go = new GameObject("HeldShell");
                go.layer = leftArm.hand.gameObject.layer;
                go.transform.SetParent(leftArm.hand, false);
                heldShell = go.AddComponent<MeshFilter>();
                go.AddComponent<MeshRenderer>();
            }
            heldShell.sharedMesh = data.shellMesh;
            heldShell.GetComponent<MeshRenderer>().sharedMaterial = data.shellMaterial;
            // Data metres to the hand's local units: the weapon bone's scale is the body's.
            float scale = weaponBone.lossyScale.x / leftArm.hand.lossyScale.x;
            heldShell.transform.localScale = Vector3.one * scale;
            heldShell.transform.SetLocalPositionAndRotation(data.shell.position * scale, data.shell.rotation);
            if (!heldShell.gameObject.activeSelf) heldShell.gameObject.SetActive(true);
        }

        private static void Sample(GripPose[] poses, float frame, out Vector3 position, out Quaternion rotation)
        {
            float f = Mathf.Clamp(frame, 0f, poses.Length - 1);
            int a = (int)f, b = Mathf.Min(a + 1, poses.Length - 1);
            position = Vector3.Lerp(poses[a].position, poses[b].position, f - a);
            rotation = Quaternion.Slerp(poses[a].rotation, poses[b].rotation, f - a);
        }

        private Transform[] FingerBones(FingerPose[] fingers) =>
            fingerBones.TryGetValue(fingers, out var bones) ? bones : ResolveBones(fingers, System.Array.ConvertAll(fingers, d => d.bone));

        private Transform[] FingerBones(string[] names) => fingerBones.TryGetValue(names, out var bones) ? bones : ResolveBones(names, names);

        private Transform[] ResolveBones(object key, string[] names)
        {
            if (bonesByName == null)
            {
                bonesByName = new System.Collections.Generic.Dictionary<string, Transform>();
                foreach (var t in pose.GetComponentsInChildren<Transform>(true)) bonesByName[t.name] = t;
            }
            var bones = System.Array.ConvertAll(names, n => n != null && bonesByName.TryGetValue(n, out var t) ? t : null);
            fingerBones[key] = bones;
            return bones;
        }

        /// <summary>Weapon root rotation with the bore (root +X) along <paramref name="bore"/> and its top toward <paramref name="top"/>.</summary>
        private static Quaternion Frame(Vector3 bore, Vector3 top) => Quaternion.LookRotation(bore, top) * Quaternion.Euler(0, -90, 0);

        /// <summary>
        /// Places the weapon on its fitted frame, blended from the low-ready to the shouldered aim, then both
        /// hands on its grips. Reload rolls the weapon toward the support hand, which leaves it for the
        /// magazine; hit clips leave the weapon in the low-ready on the reacting chest.
        /// </summary>
        /// <param name="carried">Action clip weight: the weapon then rides the animated palm line.</param>
        /// <param name="muzzle">Muzzle point in weapon root space.</param>
        private void HoldWeapon(Transform root, WeaponHold hold, float scale, Vector3 muzzle, float recoil, float reload, float pitch, Vector3? aimPoint, float carried)
        {
            float t = holdAim, k = shoulderWidth * pose.lossyScale.x / WeaponHold.HeroShoulderWidth;
            Vector3 up = pose.up;
            Quaternion chestRotation = Chest.rotation * chestBindInverse;
            Vector3 shoulders = (leftUpperArm.position + rightUpperArm.position) * .5f;
            // The clip's hands: the support hand returns to them for the magazine; action clips swing the weapon.
            Vector3 leftAnimated = leftHand.position, leftAnimatedBend = Bend(leftArm);
            Quaternion leftAnimatedRotation = leftHand.rotation, rightAnimated = rightHand.rotation;
            Vector3 palmLine = supportSocket.position - gripSocket.position, palmAnchor = gripSocket.position;

            WeaponHold.Mode ready = hold.ready, aim = hold.aim;
            Vector3 aimLine = Quaternion.AngleAxis(-pitch, pose.right) * pose.forward;
            Vector3 butt = Vector3.Lerp(shoulders + chestRotation * (ready.butt * k), rightUpperArm.position + chestRotation * (aim.butt * k), t);
            Quaternion rotation = Quaternion.Slerp(Frame(chestRotation * ready.forward, chestRotation * ready.up),
                Frame(aimLine, Quaternion.LookRotation(aimLine, up) * aim.up), t);
            if (aimPoint.HasValue && t > 0f)
            {
                // The aim line above is taken from the shoulder; the shouldered bore turns onto the crosshair point itself.
                Vector3 lever = (muzzle - WeaponHold.ToRoot(hold.butt) * scale) * root.lossyScale.x;
                for (int pass = 0; pass < 2; pass++)
                {
                    Vector3 wanted = aimPoint.Value - (butt + rotation * lever);
                    if (wanted.sqrMagnitude < 1f) break;
                    Quaternion.FromToRotation(rotation * Vector3.right, wanted).ToAngleAxis(out float angle, out Vector3 axis);
                    if (angle > 180f) angle -= 360f;
                    rotation = Quaternion.AngleAxis(Mathf.Clamp(angle, -MaxBoreTurn, MaxBoreTurn) * t, axis) * rotation;
                }
            }
            // Breathing sway on the shouldered bore; recoil kicks along it and lifts the muzzle.
            rotation = Quaternion.AngleAxis(Mathf.Sin(idleTime * 1.7f) * .25f * t, up) *
                       Quaternion.AngleAxis(Mathf.Sin(idleTime * 1.1f + 1f) * .2f * t, pose.right) * rotation;
            Vector3 bore = rotation * Vector3.right, lift = Vector3.Cross(bore, rotation * Vector3.up);
            rotation = Quaternion.AngleAxis(recoil * RecoilRise, lift) * rotation;
            butt -= bore * (recoil * RecoilKick * k);
            float reloadArc = reload >= 0f ? Mathf.Sin(Mathf.Clamp01(reload) * Mathf.PI) * reloadWeight : 0f;
            if (reloadArc > 0f)
            {
                rotation = Quaternion.AngleAxis(reloadArc * 25f, bore) * Quaternion.AngleAxis(reloadArc * 10f, lift) * rotation;
                butt += up * (reloadArc * .04f * k);
            }
            root.rotation = rotation;
            root.position += butt - root.TransformPoint(WeaponHold.ToRoot(hold.butt) * scale);
            if (carried > .001f && palmLine.sqrMagnitude > 1e-4f)
            {
                // Action clips keep the weapon on the animated palm line.
                Vector3 placed = root.position;
                Quaternion swung = Frame(palmLine, rightAnimated * weaponUpInHand);
                root.rotation = swung;
                Vector3 swungPosition = root.position + palmAnchor - root.TransformPoint(WeaponHold.ToRoot(hold.grip) * scale);
                root.rotation = Quaternion.Slerp(rotation, swung, carried);
                root.position = Vector3.Lerp(placed, swungPosition, carried);
            }

            // Support hand leaves the fore-end for the magazine during the reload clip.
            float support = reload >= 0f ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.04f, .22f, reload)) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.96f, .78f, reload)) : 1f;
            support = Mathf.Lerp(1f, support, reloadWeight);
            // Clips that move the shoulders (run, hit) must not leave a grip out of the arm's reach: the weapon comes back.
            KeepInReach(rightArm, root, scale, hold.grip, ready.right, aim.right, t);
            if (support > 0f) KeepInReach(leftArm, root, scale, hold.support, ready.left, aim.left, t);
            HoldHand(rightArm, root, scale, hold.grip, ready.right, aim.right, t,
                chestRotation * Vector3.Lerp(ready.rightBend, aim.rightBend, t), 1f, default, default, default);
            HoldHand(leftArm, root, scale, hold.support, ready.left, aim.left, t,
                chestRotation * Vector3.Lerp(ready.leftBend, aim.leftBend, t), support, leftAnimated, leftAnimatedRotation, leftAnimatedBend);
            if (carried < .01f) weaponUpInHand = Quaternion.Inverse(rightHand.rotation) * (root.rotation * Vector3.up);
        }

        /// <summary>
        /// One hand on its weapon socket with the fitted wrist position, grasp and finger curl (scaled to this
        /// rig's hand); a <paramref name="weight"/> below one returns it toward the clip's hand.
        /// </summary>
        private void HoldHand(Arm arm, Transform root, float scale, Vector3 socketNative, WeaponHold.Hand ready, WeaponHold.Hand aim, float t,
            Vector3 bend, float weight, Vector3 animatedWrist, Quaternion animatedRotation, Vector3 animatedBend)
        {
            if (weight <= 0f) return;
            Vector3 wrist = HandTarget(arm, root, scale, socketNative, ready, aim, t, out Vector3 socket, out Quaternion rotation);
            // The hand's socket marks where it holds the weapon.
            Vector3 offset = Quaternion.Inverse(rotation) * (socket - wrist);
            arm.socket.localPosition = offset / arm.hand.lossyScale.x;
            if (weight < 1f)
            {
                wrist = Vector3.Lerp(animatedWrist, wrist, weight);
                rotation = Quaternion.Slerp(animatedRotation, rotation, weight);
                bend = Vector3.Lerp(animatedBend, bend, weight);
                socket = wrist + rotation * offset;
            }
            PlaceHand(arm, socket, wrist, rotation, bend);
            // A trigger finger rests along the receiver in the low-ready and on the trigger when shouldered.
            Curl(arm, Vector2.Lerp(ready.curl, aim.curl, t), Vector4.Lerp(ready.thumbCurl, aim.thumbCurl, t),
                arm == rightArm ? Mathf.Lerp(.15f, .6f, t) : 1f, weight);
        }

        /// <summary>Wrist position (returned), socket point and hand rotation of the fitted grasp on the placed weapon.</summary>
        private static Vector3 HandTarget(Arm arm, Transform root, float scale, Vector3 socketNative, WeaponHold.Hand ready, WeaponHold.Hand aim,
            float t, out Vector3 socket, out Quaternion rotation)
        {
            socket = root.TransformPoint(WeaponHold.ToRoot(socketNative) * scale);
            Vector3 wrist = root.TransformPoint(Vector3.Lerp(WeaponHold.ToRoot(ready.wrist), WeaponHold.ToRoot(aim.wrist), t) * scale);
            Quaternion grasp = Quaternion.Slerp(
                Quaternion.LookRotation(WeaponHold.ToRoot(ready.knuckles), WeaponHold.ToRoot(ready.thumb)),
                Quaternion.LookRotation(WeaponHold.ToRoot(aim.knuckles), WeaponHold.ToRoot(aim.thumb)), t);
            rotation = root.rotation * grasp * arm.handFrameInverse;
            return socket + (wrist - socket) * arm.handScale;
        }

        /// <summary>Moves the weapon toward the shoulder by however far its grasp lies beyond the arm's reach.</summary>
        private static void KeepInReach(Arm arm, Transform root, float scale, Vector3 socketNative, WeaponHold.Hand ready, WeaponHold.Hand aim, float t)
        {
            Vector3 shoulder = arm.upper.position;
            float reach = ((arm.lower.position - shoulder).magnitude + (arm.hand.position - arm.lower.position).magnitude) * .985f;
            Vector3 d = HandTarget(arm, root, scale, socketNative, ready, aim, t, out _, out _) - shoulder;
            float excess = d.magnitude - reach;
            if (excess > 0f) root.position -= d.normalized * excess;
        }

        /// <param name="carried">Action clip weight: the weapon then rides the animated right hand along the
        /// animated palm line, and the support hand is solved onto it.</param>
        private void ClipHold(Transform root, Transform grip, Transform support, float recoil, float reload, float carried)
        {
            if (profile.guardHold)
            {
                HoldInFist(root, grip);
                return;
            }
            float reloadArc = reload >= 0f ? Mathf.Sin(Mathf.Clamp01(reload) * Mathf.PI) : 0f;
            // Reload and hit clips move the hands away from the aim hold; the weapon stays on the
            // chest-relative aim grip (lowered and canted for the magazine change) and the hands follow it.
            float held = Mathf.Max(reloadWeight, hitTime >= 0f ? 1f : 0f) * (1f - carried);
            bool aimHold = held < .01f && carried < .01f;
            Transform chestBone = Chest;
            Vector3 liveAnchor = gripSocket.position;
            if (aimHold || !anchorKnown)
            {
                aimAnchor = chestBone.InverseTransformPoint(liveAnchor);
                anchorKnown = true;
            }
            Vector3 aimHoldPoint = chestBone.TransformPoint(aimAnchor) - pose.up * (reloadArc * .07f);
            Vector3 anchor = Vector3.Lerp(liveAnchor, aimHoldPoint, held) - pose.forward * (recoil * .035f);
            // Native weapon +X is the barrel; aim it exactly along the body facing.
            Quaternion aimed = pose.rotation * Quaternion.Euler(0, -90, 0) * Quaternion.Euler(0, 0, recoil * 6f - reloadArc * 18f) * Quaternion.Euler(reloadArc * 22f, 0, 0);
            Vector3 palmLine = supportSocket.position - gripSocket.position;
            if (aimHold) weaponUpInHand = Quaternion.Inverse(rightHand.rotation) * (aimed * Vector3.up);
            // A guard holds the weapon upright in its swing plane; the swing and hit clips keep that grip.
            bool guard = profile.guardHold && meleeWeight < .01f && hitTime < 0f && palmLine.sqrMagnitude > 1e-4f;
            if (guard) weaponUpInHand = Quaternion.Inverse(rightHand.rotation) * Vector3.ProjectOnPlane(pose.up, palmLine).normalized;
            if (carried > .001f && palmLine.sqrMagnitude > 1e-4f)
            {
                // Action clips keep both hands on one rigid weapon line; the barrel follows it.
                Quaternion swung = Quaternion.LookRotation(palmLine, rightHand.rotation * weaponUpInHand) * Quaternion.Euler(0, -90, 0);
                aimed = Quaternion.Slerp(aimed, swung, carried);
            }
            root.rotation = aimed;
            root.position += anchor - grip.position;

            // In the aim hold, hands keep their clip orientation relative to the rifle line they
            // held; that grip, relative to the weapon, is reused while reload/hit clips move them.
            Vector3 animatedLine = supportSocket.position - gripSocket.position;
            Vector3 weaponLine = support.position - grip.position;
            Quaternion delta = animatedLine.sqrMagnitude > 1e-6f ? Quaternion.FromToRotation(animatedLine, weaponLine) : Quaternion.identity;
            Quaternion rightRotation = delta * rightHand.rotation;
            Quaternion leftRotation = delta * leftHand.rotation;
            if (aimHold || guard || !gripKnown)
            {
                rightGrip = Quaternion.Inverse(root.rotation) * rightRotation;
                leftGrip = Quaternion.Inverse(root.rotation) * leftRotation;
                gripKnown = true;
            }
            rightRotation = Quaternion.Slerp(rightRotation, root.rotation * rightGrip, Mathf.Max(held, carried));
            leftRotation = Quaternion.Slerp(leftRotation, root.rotation * leftGrip, Mathf.Max(held, carried));
            Vector3 leftAnimated = leftHand.position;
            Quaternion leftAnimatedRotation = leftHand.rotation;

            SolveHand(rightArm, grip.position, rightRotation, 1f, rightHand.position, rightHand.rotation);
            // Support hand leaves the fore-end for the magazine during the reload clip.
            float support01 = reload >= 0f ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.04f, .22f, reload)) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.96f, .78f, reload)) : 1f;
            support01 = Mathf.Lerp(1f, support01, reloadWeight);
            SolveHand(leftArm, support.position, leftRotation, support01, leftAnimated, leftAnimatedRotation);
        }

        /// <summary>A one-handed blade: its grip in the right fist, the blade out of the thumb side and leaning toward the
        /// knuckles, the flat facing the palm. The arms keep their clips, so guard, swing and flinch move it with the hand.</summary>
        private void HoldInFist(Transform root, Transform grip)
        {
            Transform index = Bone(HumanBodyBones.RightIndexProximal), middle = Bone(HumanBodyBones.RightMiddleProximal);
            if (index == null || middle == null) return;
            Vector3 along = ((index.position + middle.position) * .5f - rightHand.position).normalized;
            Vector3 thumbSide = Vector3.ProjectOnPlane(index.position - middle.position, along).normalized;
            // Native +X is the blade, +Y its edge.
            root.rotation = Quaternion.LookRotation(thumbSide + along * BladeLean, along) * Quaternion.Euler(0, -90, 0);
            root.position += gripSocket.position - grip.position;
            // The clips' hands are open round a rifle grip: the fist closes round the handle.
            foreach (var joint in fist) joint.bone.localRotation = joint.curl * joint.bind;
        }

        /// <summary>The right hand's fist round a blade's handle: each finger joint's bind rotation turned toward the palm,
        /// read at bind (fingers open).</summary>
        private FistJoint[] BindFist()
        {
            var joints = new List<FistJoint>();
            Transform index = Bone(HumanBodyBones.RightIndexProximal), middle = Bone(HumanBodyBones.RightMiddleProximal);
            Transform thumb = Bone(HumanBodyBones.RightThumbIntermediate);
            if (index == null || middle == null || thumb == null) return joints.ToArray();
            Vector3 along = ((index.position + middle.position) * .5f - rightHand.position).normalized;
            Vector3 palm = Vector3.Cross(along, Vector3.ProjectOnPlane(index.position - middle.position, along));
            if (Vector3.Dot(palm, thumb.position - rightHand.position) < 0f) palm = -palm;
            BindCurl(joints, palm.normalized, FistCurl, HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal);
            BindCurl(joints, palm.normalized, FistCurl, HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal);
            BindCurl(joints, palm.normalized, ThumbCurl, HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal);
            return joints.ToArray();
        }

        private void BindCurl(List<FistJoint> joints, Vector3 palm, Vector3 degrees, HumanBodyBones first, HumanBodyBones second, HumanBodyBones third)
        {
            Transform a = Bone(first), b = Bone(second), c = Bone(third);
            if (a == null || b == null || c == null) return;
            // The tip bone has no child to aim at: its segment continues the middle one.
            Vector3[] segments = { b.position - a.position, c.position - b.position, c.position - b.position };
            Transform[] bones = { a, b, c };
            for (int i = 0; i < 3; i++)
            {
                // Turned about the bend axis in the parent's frame, so the curls of a chain add up.
                Vector3 axis = Quaternion.Inverse(bones[i].parent.rotation) * Vector3.Cross(segments[i], palm).normalized;
                joints.Add(new FistJoint { bone = bones[i], bind = bones[i].localRotation, curl = Quaternion.AngleAxis(degrees[i], axis) });
            }
        }

        /// <summary>Clip-led hold: the palm socket onto <paramref name="target"/>, keeping the clip's elbow bend.</summary>
        private static void SolveHand(Arm arm, Vector3 target, Quaternion rotation, float weight, Vector3 animatedPosition, Quaternion animatedRotation)
        {
            if (weight <= 0f) return;
            Quaternion finalRotation = Quaternion.Slerp(animatedRotation, rotation, weight);
            arm.hand.rotation = finalRotation;
            Vector3 offset = arm.socket.position - arm.hand.position;
            Vector3 wrist = Vector3.Lerp(animatedPosition, target - offset, weight);
            PlaceHand(arm, wrist + offset, wrist, finalRotation, Bend(arm));
        }

        /// <summary>Direction the elbow bends away from the shoulder-wrist line.</summary>
        private static Vector3 Bend(Arm arm)
        {
            Vector3 shoulder = arm.upper.position;
            return Vector3.ProjectOnPlane(arm.lower.position - shoulder, arm.hand.position - shoulder).normalized;
        }

        /// <summary>
        /// Arm onto <paramref name="wrist"/> with the hand at <paramref name="rotation"/>, the wrist kept in its
        /// range: a hand that would flex or deviate too far is limited, and the wrist moves so the hand's
        /// socket stays on <paramref name="socket"/>.
        /// </summary>
        private static void PlaceHand(Arm arm, Vector3 socket, Vector3 wrist, Quaternion rotation, Vector3 bend)
        {
            Vector3 offset = Quaternion.Inverse(rotation) * (socket - wrist);
            for (int pass = 0; pass < 2; pass++)
            {
                SolveArm(arm, wrist, rotation, bend);
                Vector3 angles = WristAngles(arm, rotation, out Quaternion twist, out Quaternion distal);
                if (Mathf.Abs(angles.x) <= MaxFlexion + .5f && Mathf.Abs(angles.y) <= MaxDeviation + .5f) return;
                Vector3 swing = arm.flexAxis * Mathf.Clamp(angles.x, -MaxFlexion, MaxFlexion) +
                                arm.devAxis * Mathf.Clamp(angles.y, -MaxDeviation, MaxDeviation);
                Quaternion limited = swing.sqrMagnitude > 1e-8f ? Quaternion.AngleAxis(swing.magnitude, swing.normalized) : Quaternion.identity;
                rotation = distal * (limited * twist) * arm.handBind;
                wrist = socket - rotation * offset;
            }
            SolveArm(arm, wrist, rotation, bend);
        }

        /// <summary>
        /// Turns the clavicle toward a wrist target beyond the arm's reach, up to <see cref="MaxShoulderReach"/> degrees,
        /// until the arm reaches it. Humanoid playback can leave the shoulder a few centimetres farther from a reload
        /// target than the source clip did; this keeps the hand on the target instead of short of it.
        /// </summary>
        private static void ShoulderReach(Arm arm, Transform clavicle, Vector3 wrist)
        {
            if (clavicle == null) return;
            float length = ((arm.lower.position - arm.upper.position).magnitude + (arm.hand.position - arm.lower.position).magnitude) * .995f;
            if ((wrist - arm.upper.position).magnitude <= length) return;
            Vector3 pivot = clavicle.position;
            Vector3 axis = Vector3.Cross(arm.upper.position - pivot, wrist - pivot);
            if (axis.sqrMagnitude < 1e-10f) return;
            axis.Normalize();
            Quaternion start = clavicle.rotation;
            float low = 0f, high = Mathf.Min(MaxShoulderReach, Vector3.Angle(arm.upper.position - pivot, wrist - pivot));
            for (int i = 0; i < 12; i++)
            {
                float mid = (low + high) * .5f;
                clavicle.rotation = Quaternion.AngleAxis(mid, axis) * start;
                if ((wrist - arm.upper.position).magnitude > length) low = mid; else high = mid;
            }
            clavicle.rotation = Quaternion.AngleAxis(high, axis) * start;
        }

        /// <summary>
        /// Two-bone solve from the bind pose (no inherited upper-arm twist): the elbow toward <paramref name="bend"/>, then
        /// the hand set. The forearm's roll (the hand's twist beyond the wrist's share, see <see cref="Roll"/>) goes to the
        /// roll bone when the arm has one (<see cref="DriveTwist"/>), keeping the elbow a hinge; otherwise the lower arm rolls.
        /// </summary>
        private static void SolveArm(Arm arm, Vector3 wrist, Quaternion rotation, Vector3 bend)
        {
            Transform upper = arm.upper, lower = arm.lower;
            upper.localRotation = arm.upperBind;
            lower.localRotation = arm.lowerBind;
            Vector3 s = upper.position, e = lower.position;
            float l1 = (e - s).magnitude, l2 = (arm.hand.position - e).magnitude;
            Vector3 d = wrist - s;
            float distance = d.magnitude;
            if (distance > 1e-4f && l1 > 1e-5f && l2 > 1e-5f)
            {
                Vector3 u = d / distance;
                float reach = Mathf.Min(distance, (l1 + l2) * .999f);
                Vector3 side = Vector3.ProjectOnPlane(bend, u);
                if (side.sqrMagnitude < 1e-8f) side = Vector3.ProjectOnPlane(e - s, u);
                side.Normalize();
                float along = (l1 * l1 - l2 * l2 + reach * reach) / (2f * reach);
                Vector3 elbow = s + u * along + side * Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - along * along));
                upper.rotation = Quaternion.FromToRotation(e - s, elbow - s) * upper.rotation;
                e = lower.position;
                lower.rotation = Quaternion.FromToRotation(arm.hand.position - e, wrist - e) * lower.rotation;
            }
            if (arm.twist == null)
                lower.rotation = Quaternion.AngleAxis(Roll(arm, WristAngles(arm, rotation, out _).z), lower.rotation * arm.twistAxis) * lower.rotation;
            arm.hand.rotation = rotation;
        }

        /// <summary>Forearm roll for a hand twisted <paramref name="twist"/> degrees off the lower arm: half of it, all but the wrist's share when large.</summary>
        private static float Roll(Arm arm, float twist) => twist - Mathf.Clamp(twist * .5f, -arm.keep, arm.keep);

        /// <summary>Twist (degrees) of rotation <paramref name="d"/> about <paramref name="axis"/>.</summary>
        private static float Twist(Quaternion d, Vector3 axis)
        {
            if (d.w < 0f) d = new Quaternion(-d.x, -d.y, -d.z, -d.w);
            return 2f * Mathf.Atan2(Vector3.Dot(new Vector3(d.x, d.y, d.z), axis), d.w) * Mathf.Rad2Deg;
        }

        private static Vector3 WristAngles(Arm arm, Quaternion rotation, out Quaternion twistRotation) =>
            WristAngles(arm, rotation, out twistRotation, out _);

        /// <summary>
        /// (flexion, deviation, twist) of a hand rotation relative to the distal forearm, degrees from bind. The distal forearm
        /// (<paramref name="distal"/>) is the lower arm, turned by the roll bone's share of the twist when the arm has one.
        /// </summary>
        private static Vector3 WristAngles(Arm arm, Quaternion rotation, out Quaternion twistRotation, out Quaternion distal)
        {
            Quaternion d = Quaternion.Inverse(arm.lower.rotation) * rotation * Quaternion.Inverse(arm.handBind);
            Quaternion roll = arm.twist != null ? Quaternion.AngleAxis(Roll(arm, Twist(d, arm.twistAxis)), arm.twistAxis) : Quaternion.identity;
            distal = arm.lower.rotation * roll;
            d = Quaternion.Inverse(roll) * d;
            if (d.w < 0f) d = new Quaternion(-d.x, -d.y, -d.z, -d.w);
            float twist = Twist(d, arm.twistAxis);
            twistRotation = Quaternion.AngleAxis(twist, arm.twistAxis);
            (d * Quaternion.Inverse(twistRotation)).ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            Vector3 swing = axis * angle;
            return new Vector3(Vector3.Dot(swing, arm.flexAxis), Vector3.Dot(swing, arm.devAxis), twist);
        }

        /// <summary>Finger and thumb curl from bind; <paramref name="trigger"/> scales an index finger's curl.</summary>
        private static void Curl(Arm arm, Vector2 curl, Vector4 thumb, float trigger, float weight)
        {
            foreach (var digit in arm.digits)
            {
                Quaternion target;
                if (digit.thumb)
                {
                    Vector2 joint = digit.joint == 0 ? new Vector2(thumb.x, thumb.y) : digit.joint == 1 ? new Vector2(thumb.z, thumb.w) : new Vector2(0f, thumb.w * .5f);
                    target = digit.bind * Quaternion.AngleAxis(joint.x, digit.across) * Quaternion.AngleAxis(joint.y, digit.flex);
                }
                else
                {
                    float angle = (digit.joint == 0 ? curl.x : curl.y) * digit.share * (digit.index ? trigger : 1f);
                    target = digit.bind * Quaternion.AngleAxis(angle, digit.flex);
                }
                digit.bone.localRotation = weight >= 1f ? target : Quaternion.Slerp(digit.bone.localRotation, target, weight);
            }
        }

        /// <summary>Raises the leg until no measured sole point is below the floor; the clip's foot tilt is kept.</summary>
        private void GroundFoot(Transform upper, Transform lower, Transform foot, Transform toes, SolePoint[] sole)
        {
            if (upper == null || lower == null || foot == null) return;
            float ground = pose.position.y;
            float lift = 0f;
            if (sole.Length > 0)
            {
                foreach (var point in sole)
                {
                    Transform bone = point.toes && toes != null ? toes : foot;
                    lift = Mathf.Max(lift, ground - bone.TransformPoint(point.local).y);
                }
            }
            else
            {
                lift = Mathf.Max(0f, ground + footHeight - foot.position.y);
                if (toes != null) lift = Mathf.Max(lift, ground + toeHeight * .6f - toes.position.y);
            }
            if (lift < .001f) return;
            Quaternion rotation = foot.rotation;
            SolveTwoBone(upper, lower, foot, foot.position + Vector3.up * lift);
            foot.rotation = rotation;
        }

        /// <summary>Analytic two-bone IK that keeps the current bend plane.</summary>
        private static void SolveTwoBone(Transform a, Transform b, Transform c, Vector3 target)
        {
            Vector3 aPos = a.position, bPos = b.position, cPos = c.position;
            float lab = (bPos - aPos).magnitude, lcb = (cPos - bPos).magnitude;
            if (lab < 1e-5f || lcb < 1e-5f) return;
            float lat = Mathf.Clamp((target - aPos).magnitude, .001f, lab + lcb - .0005f);
            Vector3 ac = cPos - aPos, ab = bPos - aPos, at = target - aPos;
            float acab0 = Angle(ac, ab), babc0 = Angle(aPos - bPos, cPos - bPos), acat0 = Angle(ac, at);
            float acab1 = Mathf.Acos(Mathf.Clamp((lcb * lcb - lab * lab - lat * lat) / (-2f * lab * lat), -1f, 1f));
            float babc1 = Mathf.Acos(Mathf.Clamp((lat * lat - lab * lab - lcb * lcb) / (-2f * lab * lcb), -1f, 1f));
            Vector3 axis0 = Vector3.Cross(ac, ab);
            if (axis0.sqrMagnitude < 1e-10f) axis0 = Vector3.Cross(ac, b.up);
            axis0.Normalize();
            Vector3 axis1 = Vector3.Cross(ac, at);
            Quaternion aRot = a.rotation, bRot = b.rotation;
            b.localRotation *= Quaternion.AngleAxis((babc1 - babc0) * Mathf.Rad2Deg, Quaternion.Inverse(bRot) * axis0);
            a.localRotation *= Quaternion.AngleAxis((acab1 - acab0) * Mathf.Rad2Deg, Quaternion.Inverse(aRot) * axis0);
            if (axis1.sqrMagnitude > 1e-10f)
                a.rotation = Quaternion.AngleAxis(acat0 * Mathf.Rad2Deg, axis1.normalized) * a.rotation;
        }

        private static float Angle(Vector3 x, Vector3 y)
        {
            float d = x.magnitude * y.magnitude;
            return d < 1e-10f ? 0f : Mathf.Acos(Mathf.Clamp(Vector3.Dot(x, y) / d, -1f, 1f));
        }

        // Cardinal weights for a unit direction (x right, y forward), L1-normalised.
        private static void DirectionWeights(Vector2 d, float[] w)
        {
            float sum = Mathf.Abs(d.x) + Mathf.Abs(d.y);
            if (sum < 1e-5f) { d = Vector2.up; sum = 1f; }
            w[(int)Loco.Idle] = 0f;
            w[(int)Loco.Forward] = Mathf.Max(0f, d.y) / sum;
            w[(int)Loco.Backward] = Mathf.Max(0f, -d.y) / sum;
            w[(int)Loco.Right] = Mathf.Max(0f, d.x) / sum;
            w[(int)Loco.Left] = Mathf.Max(0f, -d.x) / sum;
        }

        /// <summary>Planted-foot speed the blended clips carry at 1x for a leg-relative direction.</summary>
        private float Capacity(Vector2 d)
        {
            float sum = Mathf.Abs(d.x) + Mathf.Abs(d.y);
            if (sum < 1e-5f) return locoSpeed[(int)Loco.Forward];
            return (Mathf.Max(0f, d.y) * locoSpeed[(int)Loco.Forward] + Mathf.Max(0f, -d.y) * locoSpeed[(int)Loco.Backward] +
                    Mathf.Max(0f, d.x) * locoSpeed[(int)Loco.Right] + Mathf.Max(0f, -d.x) * locoSpeed[(int)Loco.Left]) / sum;
        }

        /// <summary>
        /// Leg yaw that lets the clips carry the travel with the least foot slide, at a small
        /// cost per degree of twist; the chest twist it implies stays inside the anatomical bound.
        /// </summary>
        private float ChooseLegYaw(Vector2 v, float speed)
        {
            float best = 0f, bestCost = float.MaxValue;
            for (float yaw = -MaxLegYaw; yaw <= MaxLegYaw + .01f; yaw += 7.5f)
            {
                float capacity = Capacity(Rotate(v / speed, -yaw));
                if (capacity < .01f) continue;
                float needed = speed / capacity;
                float slide = Mathf.Max(0f, needed - profile.maxPlaybackRate) / needed;
                float cost = slide + .1f * Mathf.Max(0f, needed - 1f) + .002f * Mathf.Abs(yaw) + .0005f * Mathf.Abs(Mathf.DeltaAngle(yaw, legYaw));
                if (cost < bestCost) { bestCost = cost; best = yaw; }
            }
            return best;
        }

        // Rotates a planar (x right, y forward) vector by degrees about up (positive = toward +x).
        private static Vector2 Rotate(Vector2 d, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(d.x * c + d.y * s, -d.x * s + d.y * c);
        }

        public void ReleaseGraph()
        {
            if (graph.IsValid()) graph.Destroy();
            graph = default;
            if (ownedMask != null) Object.Destroy(ownedMask);
            if (hitMask != null) Object.Destroy(hitMask);
            ownedMask = hitMask = null;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            ReleaseGraph();
        }
    }
}
