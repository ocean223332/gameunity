#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using TienTuyen.Presentation;
using UnityEditor;
using UnityEngine;

namespace TienTuyen.Editor
{
    /// <summary>
    /// Converts one body's hand correction (output/hero-valve format, hand_correction.json) for one gun into a
    /// <see cref="HandGripData"/> asset. The file is in Source axes (xdR rest.json): positions in metres, quaternions
    /// [x, y, z, w], grip frames and tracks in weapon_bone space, finger deltas in each finger bone's space over the source
    /// take's rotation (the xdR takes in <see cref="SourceTakes"/>, which are baked in as the base: Humanoid playback does not
    /// keep the source fingers). The FBX import mirrors X, so a local position (x, y, z) becomes (−x, y, z) and a rotation
    /// (x, y, z, w) becomes (x, −y, −z, w).
    /// </summary>
    public static class HandGripImporter
    {
        /// <summary>Repo folder whose takes_* folders hold the xdR source takes (take name + .json).</summary>
        public const string SourceTakes = "output/clip-source/xdr-csgo-anims";
        /// <summary>The take whose fingers base the static grip: the source fingers are the same in every non-reload take.</summary>
        private const string GripTake = "stand_aim";

        /// <param name="json">Absolute path of the body's correction file.</param>
        /// <param name="gun">Gun key in the file (ak47, mp5sd, xm1014).</param>
        /// <param name="takes">The clip each tracked take plays as (reloads); tracks of takes not listed are skipped.</param>
        public static HandGripData Import(string json, string gun, string assetPath, IReadOnlyDictionary<string, AnimationClip> takes)
        {
            var root = JObject.Parse(File.ReadAllText(json));
            var model = (JObject)(root["model"] ?? root);
            var runtime = (JObject)model["runtime"];
            var frames = (JObject)runtime["grip_frames"]?[gun] ?? throw new InvalidOperationException("No grip frames for " + gun + " in " + json);
            var fingers = (JObject)model["fingers"]?[gun] ?? new JObject();

            var data = AssetDatabase.LoadAssetAtPath<HandGripData>(assetPath);
            if (data == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(assetPath));
                data = ScriptableObject.CreateInstance<HandGripData>();
                AssetDatabase.CreateAsset(data, assetPath);
            }
            data.gun = gun;
            data.right = Grip((JObject)frames["R"]);
            data.left = Grip((JObject)frames["L"]);
            var grip = Take(GripTake);
            data.rightFingers = Fingers(grip, "R", (JObject)fingers["R"]);
            data.leftFingers = Fingers(grip, "L", (JObject)fingers["L"]);

            var magazine = (JObject)(model["magazine"] ?? root["magazine"])?[gun];
            var tracks = new List<GripTrack>();
            var trackFingers = (JObject)runtime["track_fingers"] ?? new JObject();
            foreach (var take in ((JObject)runtime["tracks"] ?? new JObject()).Properties())
            {
                if (!takes.TryGetValue(take.Name, out var clip) || clip == null) continue;
                var track = new GripTrack { clip = clip };
                track.right = ((JArray)take.Value["R"]).Select(f => Pose((JArray)f)).ToArray();
                track.left = ((JArray)take.Value["L"]).Select(f => Pose((JArray)f)).ToArray();
                var source = Take(take.Name);
                (track.rightBones, track.rightFingers) = FingerRows(source, "R", (JArray)trackFingers[take.Name]?["R"], track.right.Length);
                (track.leftBones, track.leftFingers) = FingerRows(source, "L", (JArray)trackFingers[take.Name]?["L"], track.left.Length);
                if (magazine?["takes"]?[take.Name] is JObject path) Magazine(track, path, take.Name);
                tracks.Add(track);
            }
            data.tracks = tracks.ToArray();
            data.magazineBox = magazine == null ? new Bounds() : MagazineBox(magazine, gun);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            return data;
        }

        /// <summary>World gun models, w_&lt;gun&gt;.fbx, whose magazine the box must cut out.</summary>
        public const string GunDir = "Assets/_TienTuyen/Art/Weapons/World";

        /// <summary>The magazine box in Unity axes, checked to cut exactly the file's triangle count out of the gun model.</summary>
        private static Bounds MagazineBox(JObject magazine, string gun)
        {
            var min = (JArray)magazine["box"]["min"];
            var max = (JArray)magazine["box"]["max"];
            // Mirroring X swaps the box's x ends.
            var box = new Bounds();
            box.SetMinMax(Position((float)max[0], (float)min[1], (float)min[2]), Position((float)min[0], (float)max[1], (float)max[2]));
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(GunDir + "/w_" + gun + ".fbx")
                ?? throw new FileNotFoundException("No world gun w_" + gun + " in " + GunDir);
            int expected = (int)magazine["check"]["tris"], cut = MagazineSplit.Triangles(model, box);
            if (cut != expected)
                throw new InvalidOperationException("The " + gun + " magazine box cuts " + cut + " triangles, the correction says " + expected);
            return box;
        }

        /// <summary>A reload take's magazine path: release, insert, the hidden frames and one frame per frame in between.</summary>
        private static void Magazine(GripTrack track, JObject path, string take)
        {
            track.magazineRelease = (int)path["release"];
            track.magazineInsert = (int)path["insert"];
            var hidden = (JArray)path["hidden"];
            track.magazineHiddenFrom = hidden != null ? (int)hidden[0] : -1;
            track.magazineHiddenTo = hidden != null ? (int)hidden[1] : -1;
            track.magazine = ((JArray)path["track"]).Select(f => Pose((JArray)f)).ToArray();
            if (track.magazine.Length != track.magazineInsert - track.magazineRelease)
                throw new InvalidOperationException(take + ": magazine track has " + track.magazine.Length + " frames for release " +
                    track.magazineRelease + " to insert " + track.magazineInsert);
        }

        public static Vector3 Position(float x, float y, float z) => new Vector3(-x, y, z);

        public static Quaternion Rotation(float x, float y, float z, float w) => new Quaternion(x, -y, -z, w);

        private static Quaternion Rotation(JArray q, int at = 0) => Rotation((float)q[at], (float)q[at + 1], (float)q[at + 2], (float)q[at + 3]);

        private static GripPose Grip(JObject frame)
        {
            var p = (JArray)frame["pos"];
            return new GripPose(Position((float)p[0], (float)p[1], (float)p[2]), Rotation((JArray)frame["quat"]));
        }

        /// <summary>A track frame: position then quaternion, seven numbers.</summary>
        private static GripPose Pose(JArray f) => new GripPose(Position((float)f[0], (float)f[1], (float)f[2]), Rotation(f, 3));

        /// <summary>A source take: bone names and, per frame, each bone's local [quaternion, position].</summary>
        private sealed class SourceTake
        {
            public string[] bones;
            public JArray frames;

            public IEnumerable<string> Fingers(string side) => bones.Where(b => b.Contains("_" + side + "_Finger"));

            public Quaternion Local(string bone, int frame) =>
                Rotation((JArray)frames[Mathf.Clamp(frame, 0, frames.Count - 1)][Array.IndexOf(bones, bone)][0]);
        }

        private static SourceTake Take(string name)
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", SourceTakes));
            string file = Directory.GetDirectories(root, "takes_*").Select(d => Path.Combine(d, name + ".json")).FirstOrDefault(File.Exists)
                ?? throw new FileNotFoundException("No source take " + name + " under " + root);
            var take = JObject.Parse(File.ReadAllText(file));
            return new SourceTake { bones = take["bones"].ToObject<string[]>(), frames = (JArray)take["frames"] };
        }

        /// <summary>Every finger of the side: the take's first-frame rotation × the delta (if any).</summary>
        private static FingerPose[] Fingers(SourceTake take, string side, JObject deltas) => take.Fingers(side).Select(bone =>
            new FingerPose { bone = bone, rotation = take.Local(bone, 0) * (deltas?[bone]?["q"] is JArray q ? Rotation(q) : Quaternion.identity) }).ToArray();

        /// <summary>Every finger of the side over <paramref name="count"/> frames: the take's rotation × that frame's delta (any subset per frame), frames × bones rows.</summary>
        private static (string[], Quaternion[]) FingerRows(SourceTake take, string side, JArray deltas, int count)
        {
            var bones = take.Fingers(side).ToArray();
            var rows = new Quaternion[count * bones.Length];
            for (int f = 0; f < count; f++)
                for (int b = 0; b < bones.Length; b++)
                    rows[f * bones.Length + b] = take.Local(bones[b], f) *
                        (deltas != null && f < deltas.Count && deltas[f] is JObject frame && frame[bones[b]] is JArray q ? Rotation(q) : Quaternion.identity);
            return (bones, rows);
        }
    }
}
#endif
