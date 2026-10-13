using System;
using UnityEngine;

namespace TienTuyen.Presentation
{
    /// <summary>
    /// Both hands of one body on one carried gun (output/hero-valve hand_correction.json, converted by HandGripImporter):
    /// each wrist (Hand bone) is pinned to a frame in the weapon bone's space, and each finger bone takes a local rotation
    /// (the source take's × the correction's delta, baked by the importer: Humanoid playback does not keep the source
    /// fingers). A reload clip with a track uses that track's per-frame wrist frames and finger rotations instead. Unity
    /// axes, metres at body scale 1; a uniformly scaled body scales the gun and the frames with it.
    /// </summary>
    public sealed class HandGripData : ScriptableObject
    {
        /// <summary>Frames per second of every track (the source takes' rate).</summary>
        public const float TrackRate = 30f;

        [Tooltip("Gun key in the source file (ak47, mp5sd, xm1014).")]
        public string gun;
        public GripPose right, left;
        public FingerPose[] rightFingers = new FingerPose[0], leftFingers = new FingerPose[0];
        [Tooltip("Per-frame hands for the reload clips; a clip without a track keeps the static grip.")]
        public GripTrack[] tracks = new GripTrack[0];
        [Tooltip("The magazine's bounds in the weapon bone's space; empty for a gun without one. MagazineSplit cuts it out.")]
        public Bounds magazineBox;
        [Tooltip("The shell the left hand carries to a tube-fed gun's loading port (the first-person one's look); none without one.")]
        public Mesh shellMesh;
        public Material shellMaterial;
        [Tooltip("The carried shell's frame in the left Hand bone's space (its axis +Z toward the muzzle on insert).")]
        public GripPose shell;

        public bool HasMagazine => magazineBox.size != Vector3.zero;
        public bool HasShell => shellMesh != null;

        public GripTrack Track(AnimationClip clip)
        {
            if (clip == null || tracks == null) return null;
            foreach (var track in tracks) if (track != null && track.clip == clip) return track;
            return null;
        }

        /// <summary>
        /// The middle-finger bone (same joint) a ring (Finger3*) or little (Finger4*) finger bone follows, or null. The source
        /// clips have neither, so a body's ring and little fingers take their leader's turn from bind.
        /// </summary>
        public static string Leader(string bone)
        {
            foreach (string finger in new[] { "_Finger3", "_Finger4" })
            {
                int at = bone.IndexOf(finger, StringComparison.Ordinal);
                if (at >= 0) return bone.Substring(0, at) + "_Finger2" + bone.Substring(at + finger.Length);
            }
            return null;
        }
    }

    /// <summary>A wrist frame in the weapon bone's space (the shell's in the left Hand bone's).</summary>
    [Serializable]
    public struct GripPose
    {
        public Vector3 position;
        public Quaternion rotation;

        public GripPose(Vector3 position, Quaternion rotation)
        {
            this.position = position;
            this.rotation = rotation;
        }
    }

    /// <summary>A finger bone's local rotation, replacing the clip's.</summary>
    [Serializable]
    public struct FingerPose
    {
        public string bone;
        public Quaternion rotation;
    }

    /// <summary>
    /// One reload clip's hands, frame by frame at <see cref="HandGripData.TrackRate"/> from the clip's start, and the
    /// magazine's path between release and insert (it already follows the corrected left hand); or, without wrist frames,
    /// one clip's fingers alone on the static grip (fire: the gun's trigger pull). Finger local rotations are frames × bones,
    /// row major.
    /// </summary>
    [Serializable]
    public sealed class GripTrack
    {
        public AnimationClip clip;
        public GripPose[] right = new GripPose[0], left = new GripPose[0];
        public string[] rightBones = new string[0], leftBones = new string[0];
        public Quaternion[] rightFingers = new Quaternion[0], leftFingers = new Quaternion[0];
        [Tooltip("Frames the magazine leaves its well (release) and is back in it (insert); -1 when it stays seated.")]
        public int magazineRelease = -1, magazineInsert = -1;
        [Tooltip("Frames, inclusive, the magazine is out of sight (pouch swap).")]
        public int magazineHiddenFrom = -1, magazineHiddenTo = -1;
        [Tooltip("From release to insert − 1: the magazine's frame over its seated pose, in the weapon bone's space.")]
        public GripPose[] magazine = new GripPose[0];
        [Tooltip("Frames, inclusive, the left hand holds a shell (to the loading port); from > to wraps the loop; -1 for none.")]
        public int shellFrom = -1, shellTo = -1;

        public bool HoldsShell(float frame) => shellFrom >= 0 &&
            (shellFrom <= shellTo ? frame >= shellFrom && frame < shellTo + 1 : frame >= shellFrom || frame < shellTo + 1);
    }
}
