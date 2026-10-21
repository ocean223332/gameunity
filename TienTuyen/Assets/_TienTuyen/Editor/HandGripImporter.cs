#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
        /// <param name="takes">The clip each tracked take plays as (reloads, fire); tracks of takes not listed are skipped.</param>
        /// <param name="body">The body the data poses: its ring and little fingers (bones the source takes lack) take the
        /// middle finger's turn from bind, as the correction's ValveBiped does; null keeps the takes' fingers only.</param>
        /// <param name="authoredOn">The body the correction was made on, when it is not <paramref name="body"/>: its finger
        /// hands, so each of <paramref name="body"/>'s wrists is pinned where its knuckles best meet the authored body's
        /// (<see cref="Palm"/>) and its fingers take the authored fingers' shape (Carry). Null when the correction is the body's own.</param>
        public static HandGripData Import(string json, string gun, string assetPath, IReadOnlyDictionary<string, AnimationClip> takes, GameObject body = null,
            GameObject authoredOn = null)
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
            var bind = Bind(body);
            var from = authoredOn != null && authoredOn != body && body != null ? new FingerRig(authoredOn) : null;
            var to = from != null ? new FingerRig(body) : null;
            // The fingers are posed on the body the correction was made on, then carried onto this one.
            var grip = Take(GripTake, from?.bind ?? bind);
            data.rightFingers = Carry(Fingers(grip, "R", (JObject)fingers["R"]), from, to);
            data.leftFingers = Carry(Fingers(grip, "L", (JObject)fingers["L"]), from, to);
            data.right = Palm(data.right, from, to, "R");
            data.left = Palm(data.left, from, to, "L");

            var magazine = (JObject)(model["magazine"] ?? root["magazine"])?[gun];
            var shell = (JObject)model["shell"]?[gun];
            var tracks = new List<GripTrack>();
            var trackFingers = (JObject)runtime["track_fingers"] ?? new JObject();
            var gunFingers = (JObject)runtime["finger_tracks"] ?? new JObject();
            var poses = (JObject)runtime["tracks"] ?? new JObject();
            // A take with wrist frames (the reloads) moves the hands; one with only this gun's finger rows (fire) moves the fingers.
            foreach (string name in poses.Properties().Concat(gunFingers.Properties()).Select(p => p.Name).Distinct())
            {
                if (!takes.TryGetValue(name, out var clip) || clip == null) continue;
                var rows = (JObject)gunFingers[name]?[gun] ?? (JObject)trackFingers[name];
                var track = new GripTrack { clip = clip };
                if (poses[name] is JObject wrists)
                {
                    track.right = ((JArray)wrists["R"]).Select(f => Palm(Pose((JArray)f), from, to, "R")).ToArray();
                    track.left = ((JArray)wrists["L"]).Select(f => Palm(Pose((JArray)f), from, to, "L")).ToArray();
                }
                else if (rows == null) continue;
                var source = Take(name, from?.bind ?? bind);
                (track.rightBones, track.rightFingers) = Carry(FingerRows(source, "R", (JArray)rows?["R"], Count(track.right, rows, "R")), from, to);
                (track.leftBones, track.leftFingers) = Carry(FingerRows(source, "L", (JArray)rows?["L"], Count(track.left, rows, "L")), from, to);
                if (magazine?["takes"]?[name] is JObject path) Magazine(track, path, name);
                if (shell?["takes"]?[name] is JArray held) (track.shellFrom, track.shellTo) = ((int)held[0], (int)held[1]);
                tracks.Add(track);
            }
            data.tracks = tracks.ToArray();
            data.magazineBox = magazine == null ? new Bounds() : MagazineBox(magazine, gun);
            Shell(data, shell);
            // The shell is held in the left Hand bone's space: the same place on the gun from the body's own wrist.
            if (data.HasShell && to != null)
            {
                var (offset, turn) = to.Fit(from, "L");
                var inverse = Quaternion.Inverse(turn);
                data.shell = new GripPose(inverse * (data.shell.position - offset), inverse * data.shell.rotation);
            }
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

        /// <summary>First-person models, Resources/Viewmodels/&lt;weapon id&gt;/v_&lt;gun&gt;.fbx, whose loading shell the carried one copies.</summary>
        public const string ShellModel = "Assets/_TienTuyen/Art/Resources/Viewmodels/weapon.shotgun/v_xm1014.fbx";
        private const string ShellMaterialName = "shell_shotgun", ShellBone = "v_weapon.xm1014_Shell1";

        /// <summary>
        /// The carried shell: its frame in the left hand, and a copy of the first-person model's first loading shell (the
        /// <see cref="ShellMaterialName"/> triangles skinned to <see cref="ShellBone"/>) with that model's material, centred on
        /// its axis with the axis on +Z and the brass rim on −Z, kept inside the data asset. Removed when the file has none.
        /// </summary>
        private static void Shell(HandGripData data, JObject shell)
        {
            if (shell == null)
            {
                if (data.shellMesh != null) UnityEngine.Object.DestroyImmediate(data.shellMesh, true);
                data.shellMesh = null;
                data.shellMaterial = null;
                return;
            }
            data.shell = Pose((JArray)shell["pose"]);
            var assets = AssetDatabase.LoadAllAssetsAtPath(ShellModel);
            data.shellMaterial = assets.OfType<Material>().FirstOrDefault(m => m.name == ShellMaterialName)
                ?? throw new InvalidOperationException("No " + ShellMaterialName + " material in " + ShellModel);
            var model = assets.OfType<GameObject>().FirstOrDefault(g => g.transform.parent == null)
                ?? throw new FileNotFoundException("No model " + ShellModel);
            var skin = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.sharedMaterials.Contains(data.shellMaterial))
                ?? throw new InvalidOperationException("No skin with " + ShellMaterialName + " in " + ShellModel);
            var source = skin.sharedMesh;
            int bone = Array.FindIndex(skin.bones, b => b != null && b.name == ShellBone);
            int sub = Array.IndexOf(skin.sharedMaterials, data.shellMaterial);
            if (bone < 0) throw new InvalidOperationException("No " + ShellBone + " in " + ShellModel);

            // The shell's vertices at the model's rest, in the model's root space.
            var weights = source.boneWeights;
            var toRoot = model.transform.worldToLocalMatrix * skin.bones[bone].localToWorldMatrix * source.bindposes[bone];
            var map = new Dictionary<int, int>();
            var triangles = new List<int>();
            var corners = source.GetTriangles(sub);
            for (int t = 0; t < corners.Length; t += 3)
            {
                if (!Enumerable.Range(t, 3).All(k => weights[corners[k]].boneIndex0 == bone && weights[corners[k]].weight0 >= .5f)) continue;
                for (int k = t; k < t + 3; k++)
                {
                    if (!map.TryGetValue(corners[k], out int at)) map[corners[k]] = at = map.Count;
                    triangles.Add(at);
                }
            }
            if (triangles.Count == 0) throw new InvalidOperationException("No " + ShellMaterialName + " triangles on " + ShellBone + " in " + ShellModel);
            var order = map.OrderBy(e => e.Value).Select(e => e.Key).ToArray();
            var sourceVertices = source.vertices;
            var sourceNormals = source.normals;
            var points = order.Select(i => toRoot.MultiplyPoint3x4(sourceVertices[i])).ToArray();

            // The axis: the points' main direction; the rim is the wider end.
            var centroid = points.Aggregate(Vector3.zero, (a, p) => a + p) / points.Length;
            var axis = Vector3.one;
            for (int it = 0; it < 32; it++)
                axis = points.Aggregate(Vector3.zero, (a, p) => a + (p - centroid) * Vector3.Dot(p - centroid, axis)).normalized;
            float low = points.Min(p => Vector3.Dot(p, axis)), high = points.Max(p => Vector3.Dot(p, axis)), length = high - low;
            if (length < .05f || length > .09f)
                throw new InvalidOperationException(ShellModel + " shell is " + length + " long; a 12-gauge shell is about 0.07 m");
            Vector3 centre = centroid + axis * ((low + high) / 2f - Vector3.Dot(centroid, axis));
            float Width(float from, float to) => points.Where(p => Vector3.Dot(p, axis) >= from && Vector3.Dot(p, axis) <= to)
                .Select(p => Vector3.ProjectOnPlane(p - centre, axis).magnitude).DefaultIfEmpty(0f).Max();
            if (Width(low, low + length * .1f) < Width(high - length * .1f, high)) axis = -axis;
            var toShell = Quaternion.Inverse(Quaternion.LookRotation(axis, Vector3.Cross(axis, Vector3.right).sqrMagnitude > .01f ? Vector3.Cross(axis, Vector3.right) : Vector3.up));

            var mesh = data.shellMesh;
            if (mesh == null)
            {
                mesh = new Mesh();
                AssetDatabase.AddObjectToAsset(mesh, data);
            }
            mesh.Clear();
            mesh.name = data.name + "_Shell";
            mesh.vertices = points.Select(p => toShell * (p - centre)).ToArray();
            if (sourceNormals.Length > 0) mesh.normals = order.Select(i => toShell * toRoot.MultiplyVector(sourceNormals[i]).normalized).ToArray();
            var uv = new List<Vector2>();
            source.GetUVs(0, uv);
            if (uv.Count > 0) mesh.uv = order.Select(i => uv[i]).ToArray();
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            data.shellMesh = mesh;
            Debug.Log($"{data.name}: carried shell of {order.Length} vertices, {triangles.Count / 3} triangles, {length * 1000f:F0} mm");
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

        /// <summary>Frames of a track: its wrist frames, or its finger rows when it has none.</summary>
        private static int Count(GripPose[] poses, JObject rows, string side) => poses.Length > 0 ? poses.Length : ((JArray)rows[side]).Count;

        /// <summary>
        /// A source take: bone names and, per frame, each bone's local [quaternion, position]. A body finger bone the take lacks
        /// (ring Finger3*, little Finger4*) follows its middle-finger <see cref="HandGripData.Leader"/>: its bind × the leader's turn from bind.
        /// </summary>
        private sealed class SourceTake
        {
            public string[] bones;
            public JArray frames;
            private Dictionary<string, Quaternion> bind;
            private string[] followers = new string[0];

            public SourceTake Follow(Dictionary<string, Quaternion> body)
            {
                bind = body;
                if (body != null)
                    followers = body.Keys.Where(n => !bones.Contains(n) && HandGripData.Leader(n) is string l && bones.Contains(l) && body.ContainsKey(l)).ToArray();
                return this;
            }

            public IEnumerable<string> Fingers(string side) => bones.Concat(followers).Where(b => b.Contains("_" + side + "_Finger"));

            public Quaternion Local(string bone, int frame)
            {
                int at = Array.IndexOf(bones, bone);
                if (at >= 0) return Rotation((JArray)frames[Mathf.Clamp(frame, 0, frames.Count - 1)][at][0]);
                string leader = HandGripData.Leader(bone);
                return bind[bone] * Quaternion.Inverse(bind[leader]) * Local(leader, frame);
            }
        }

        /// <summary>The body's finger bones' bind local rotations by name, or null without a body.</summary>
        private static Dictionary<string, Quaternion> Bind(GameObject body) => body == null ? null :
            body.GetComponentsInChildren<Transform>(true).Where(t => t.name.Contains("_Finger")).ToDictionary(t => t.name, t => t.localRotation);

        private static SourceTake Take(string name, Dictionary<string, Quaternion> bind = null)
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", SourceTakes));
            string file = Directory.GetDirectories(root, "takes_*").Select(d => Path.Combine(d, name + ".json")).FirstOrDefault(File.Exists)
                ?? throw new FileNotFoundException("No source take " + name + " under " + root);
            var take = JObject.Parse(File.ReadAllText(file));
            return new SourceTake { bones = take["bones"].ToObject<string[]>(), frames = (JArray)take["frames"] }.Follow(bind);
        }

        /// <summary>A body's finger bones at bind: local rotations, and each bone's segment as a direction in its own frame
        /// (to the next joint; the last bone's to its farthest skinned vertex, the fingertip).</summary>
        private sealed class FingerRig
        {
            public readonly Dictionary<string, Quaternion> bind;
            public readonly Dictionary<string, Vector3> along = new Dictionary<string, Vector3>();
            // Each finger's root joint (Finger0..4) in its Hand bone's frame, by name.
            public readonly Dictionary<string, Vector3> knuckles = new Dictionary<string, Vector3>();

            public FingerRig(GameObject body)
            {
                bind = Bind(body);
                var bones = body.GetComponentsInChildren<Transform>(true).Where(t => t.name.Contains("_Finger")).ToDictionary(t => t.name);
                foreach (var bone in bones.Values)
                {
                    if (Next(bone.name) is string next && bones.TryGetValue(next, out var child)) along[bone.name] = child.localPosition.normalized;
                    if (Regex.IsMatch(bone.name, @"_Finger\d$")) knuckles[bone.name] = bone.parent.InverseTransformPoint(bone.position);
                }
                foreach (var skin in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var mesh = skin.sharedMesh;
                    if (mesh == null) continue;
                    var vertices = mesh.vertices;
                    var weights = mesh.boneWeights;
                    var bindposes = mesh.bindposes;
                    for (int b = 0; b < skin.bones.Length; b++)
                    {
                        var bone = skin.bones[b];
                        if (bone == null || along.ContainsKey(bone.name) || !bones.ContainsKey(bone.name)) continue;
                        Vector3 tip = Vector3.zero;
                        for (int v = 0; v < vertices.Length; v++)
                            if (weights[v].boneIndex0 == b && weights[v].weight0 >= .5f)
                            {
                                Vector3 p = bindposes[b].MultiplyPoint3x4(vertices[v]);
                                if (p.sqrMagnitude > tip.sqrMagnitude) tip = p;
                            }
                        if (tip != Vector3.zero) along[bone.name] = tip.normalized;
                    }
                }
            }

            /// <summary>
            /// The rigid move (in this body's hand frame: offset, then turn) that best lays this hand's knuckle roots onto
            /// <paramref name="other"/>'s (least squares, Horn's quaternion), so a point p of this hand sits at offset + turn · p
            /// in the other hand's frame.
            /// </summary>
            public (Vector3 offset, Quaternion turn) Fit(FingerRig other, string side)
            {
                var pairs = knuckles.Where(k => k.Key.Contains("_" + side + "_Finger") && other.knuckles.ContainsKey(k.Key))
                    .Select(k => (mine: k.Value, theirs: other.knuckles[k.Key])).ToArray();
                if (pairs.Length < 3) return (Vector3.zero, Quaternion.identity);
                Vector3 a = pairs.Aggregate(Vector3.zero, (sum, q) => sum + q.mine) / pairs.Length;
                Vector3 b = pairs.Aggregate(Vector3.zero, (sum, q) => sum + q.theirs) / pairs.Length;
                var m = new double[3, 3];
                foreach (var (mine, theirs) in pairs)
                {
                    Vector3 x = mine - a, y = theirs - b;
                    for (int i = 0; i < 3; i++)
                        for (int j = 0; j < 3; j++) m[i, j] += x[i] * y[j];
                }
                double sxx = m[0, 0], sxy = m[0, 1], sxz = m[0, 2], syx = m[1, 0], syy = m[1, 1], syz = m[1, 2], szx = m[2, 0], szy = m[2, 1], szz = m[2, 2];
                double[,] n =
                {
                    { sxx + syy + szz, syz - szy, szx - sxz, sxy - syx },
                    { syz - szy, sxx - syy - szz, sxy + syx, szx + sxz },
                    { szx - sxz, sxy + syx, -sxx + syy - szz, syz + szy },
                    { sxy - syx, szx + sxz, syz + szy, -sxx - syy + szz },
                };
                // The largest eigenvector by power iteration, shifted so every eigenvalue is positive.
                double shift = 0;
                for (int i = 0; i < 4; i++)
                    for (int j = 0; j < 4; j++) shift += Math.Abs(n[i, j]);
                var v = new double[] { 1, 0, 0, 0 };
                for (int it = 0; it < 500; it++)
                {
                    var w = new double[4];
                    for (int i = 0; i < 4; i++)
                    {
                        w[i] = shift * v[i];
                        for (int j = 0; j < 4; j++) w[i] += n[i, j] * v[j];
                    }
                    double length = Math.Sqrt(w.Sum(x => x * x));
                    for (int i = 0; i < 4; i++) v[i] = w[i] / length;
                }
                var turn = new Quaternion((float)v[1], (float)v[2], (float)v[3], (float)v[0]);
                return (b - turn * a, turn);
            }
        }

        /// <summary>
        /// A wrist frame (weapon space) for the authored body's hand, as the frame for <paramref name="to"/>'s hand that puts
        /// its knuckles where the authored hand's were. Kept without both rigs.
        /// </summary>
        private static GripPose Palm(GripPose wrist, FingerRig from, FingerRig to, string side)
        {
            if (from == null || to == null) return wrist;
            var (offset, turn) = to.Fit(from, side);
            return new GripPose(wrist.position + wrist.rotation * offset, wrist.rotation * turn);
        }

        /// <summary>The next bone of a finger chain (Finger1 → Finger11 → Finger12), or null after the last.</summary>
        private static string Next(string bone)
        {
            string digits = bone.Substring(bone.LastIndexOf("_Finger", StringComparison.Ordinal) + "_Finger".Length);
            return digits.Length == 1 ? bone + "1" : digits.Length == 2 && digits[1] == '1' ? bone.Substring(0, bone.Length - 1) + "2" : null;
        }

        /// <summary>
        /// One frame of finger rotations posed on <paramref name="from"/>, carried onto <paramref name="to"/> by shape: down each
        /// chain, in the hand's frame (which the grip sets alike on every body), each bone takes its bind rotation under its
        /// posed parent, swung so its segment points where the source bone's does. The bodies' bind fingers differ in curl and
        /// roll, so neither the same local rotations nor the same turn from bind give the same hand. Without both rigs the
        /// rotations are kept.
        /// </summary>
        private static Quaternion[] Carry(string[] bones, Quaternion[] locals, int offset, FingerRig from, FingerRig to)
        {
            var result = new Quaternion[bones.Length];
            Array.Copy(locals, offset, result, 0, bones.Length);
            if (from == null || to == null) return result;
            var index = bones.Select((b, i) => (b, i)).ToDictionary(e => e.b, e => e.i);
            foreach (string first in bones.Where(b => Regex.IsMatch(b, @"_Finger\d$")))
            {
                // The authored hand's frame seen from the body's hand, whose wrist frame is fitted by Palm.
                Quaternion source = Quaternion.Inverse(to.Fit(from, first.Contains("_R_Finger") ? "R" : "L").turn), target = Quaternion.identity;
                for (string bone = first; bone != null && index.TryGetValue(bone, out int i); bone = Next(bone))
                {
                    if (!from.along.TryGetValue(bone, out var fromAlong) || !to.along.TryGetValue(bone, out var toAlong) || !to.bind.TryGetValue(bone, out var rest)) break;
                    Quaternion posed = source * result[i], start = target * rest;
                    Quaternion end = Quaternion.FromToRotation(start * toAlong, posed * fromAlong) * start;
                    result[i] = Quaternion.Inverse(target) * end;
                    source = posed;
                    target = end;
                }
            }
            return result;
        }

        private static FingerPose[] Carry(FingerPose[] fingers, FingerRig from, FingerRig to)
        {
            var rotations = Carry(fingers.Select(f => f.bone).ToArray(), fingers.Select(f => f.rotation).ToArray(), 0, from, to);
            return fingers.Select((f, i) => new FingerPose { bone = f.bone, rotation = rotations[i] }).ToArray();
        }

        private static (string[], Quaternion[]) Carry((string[] bones, Quaternion[] rows) track, FingerRig from, FingerRig to)
        {
            if (track.bones.Length == 0) return track;
            for (int at = 0; at < track.rows.Length; at += track.bones.Length)
                Array.Copy(Carry(track.bones, track.rows, at, from, to), 0, track.rows, at, track.bones.Length);
            return track;
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
