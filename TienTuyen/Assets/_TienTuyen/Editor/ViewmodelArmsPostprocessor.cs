using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TienTuyen.Editor
{
    /// <summary>
    /// First-person arms that match the third-person bodies: on import of a CS:GO viewmodel (Resources/Viewmodels/*/v_*.fbx),
    /// its "Arms" mesh (gloves and sleeves) is replaced by the hero's own forearms, hands and rolled sleeve cuffs, retargeted
    /// in Blender onto the viewmodel's arm bones (output/hero-valve/_scripts/fp_arms.py) and drawn with the hero's material.
    /// Each other body's arms (<see cref="Sets"/>) go beside it on a disabled renderer "Arms_&lt;set&gt;" on the same bones, which
    /// the first-person view shows instead for a body whose profile names that set (SkinnedCharacterProfile.firstPersonArms).
    /// A set's data file is &lt;prefix&gt;&lt;viewmodel&gt;.json; without the hero's the viewmodel keeps its own arms, without
    /// another set's it has no renderer for it. The gun, its takes, sights and flash are untouched.
    /// </summary>
    public sealed class ViewmodelArmsPostprocessor : AssetPostprocessor
    {
        private const string ViewmodelFolder = "Assets/_TienTuyen/Art/Resources/Viewmodels/";
        /// <summary>Arm sets: name (null: the hero's, which replaces "Arms"), data file prefix, material.</summary>
        private static readonly (string name, string data, string material)[] Sets =
        {
            (null, HeroValveImporter.ArtDir + "/HeroArms_", HeroValveImporter.ArtDir + "/HeroValve.mat"),
            ("USSoldier", USSoldierValveImporter.ArtDir + "/USSoldierArms_", USSoldierValveImporter.ArtDir + "/USSoldierValve.mat"),
        };

        /// <summary>Layout of the data file (flat arrays; positions, normals and heads in the file's Blender world).</summary>
        [Serializable]
        private sealed class ArmsFile
        {
            public float[] positions = null, normals = null, uv = null, bone_weight = null, heads = null;
            public int[] triangles = null, bone_index = null;
            public string[] bone_names = null, head_names = null;
        }

        public override int GetPostprocessOrder() => 10;

        private void OnPostprocessModel(GameObject root)
        {
            if (!assetPath.StartsWith(ViewmodelFolder, StringComparison.Ordinal) ||
                !assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) return;
            var present = new List<(string name, string data, string material)>();
            foreach (var set in Sets)
            {
                string dataPath = set.data + Path.GetFileNameWithoutExtension(assetPath) + ".json";
                context.DependsOnSourceAsset(dataPath);
                context.DependsOnSourceAsset(set.material);
                if (File.Exists(dataPath)) present.Add((set.name, dataPath, set.material));
            }
            if (present.Count == 0) return;
            SkinnedMeshRenderer arms = null;
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.name == "Arms") arms = smr;
            if (arms == null) throw new InvalidOperationException(assetPath + " has no Arms mesh for " + present[0].data);
            var bones = new Dictionary<string, Transform>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) bones[t.name] = t;

            // Bind pose of every bone in the Arms mesh's space: the mesh's own bindposes where it has them, else the import pose.
            Matrix4x4 meshToWorld = arms.transform.localToWorldMatrix;
            var bind = new Dictionary<string, Matrix4x4>();
            var oldBones = arms.bones; var oldBind = arms.sharedMesh.bindposes;
            for (int i = 0; i < oldBones.Length; i++) if (oldBones[i] != null) bind[oldBones[i].name] = oldBind[i];
            Matrix4x4 BindPose(Transform t) => bind.TryGetValue(t.name, out var m) ? m : t.worldToLocalMatrix * meshToWorld;

            foreach (var set in present)
            {
                var (mesh, newBones) = Build(set.data, bones, bind, BindPose);
                mesh.name = set.name == null ? "HeroArms" : set.name + "Arms";
                context.AddObjectToAsset(mesh.name, mesh);
                var target = arms;
                if (set.name != null)
                {
                    // Beside "Arms", in its space, off until the first-person view picks it.
                    var holder = new GameObject("Arms_" + set.name);
                    holder.transform.SetParent(arms.transform.parent, false);
                    holder.transform.localPosition = arms.transform.localPosition;
                    holder.transform.localRotation = arms.transform.localRotation;
                    holder.transform.localScale = arms.transform.localScale;
                    target = holder.AddComponent<SkinnedMeshRenderer>();
                    target.rootBone = arms.rootBone;
                    holder.SetActive(false);
                }
                target.sharedMesh = mesh;
                target.bones = newBones;
                var material = AssetDatabase.LoadAssetAtPath<Material>(set.material);
                if (material != null) target.sharedMaterials = new[] { material };
                target.localBounds = mesh.bounds;
            }
        }

        /// <summary>One set's arms as a mesh on the viewmodel's bones, in the Arms mesh's space.</summary>
        private (Mesh mesh, Transform[] bones) Build(string dataPath, Dictionary<string, Transform> bones, Dictionary<string, Matrix4x4> bind,
            Func<Transform, Matrix4x4> BindPose)
        {
            var data = JsonUtility.FromJson<ArmsFile>(File.ReadAllText(dataPath));
            // The Blender world of the data file -> this mesh's space: a similarity fitted on the heads of the bones the old
            // arms were bound to (mirrored when the importer flips handedness, which then also flips the triangles' winding).
            var src = new List<Vector3>(); var dst = new List<Vector3>();
            for (int i = 0; i < data.head_names.Length; i++)
                if (bind.ContainsKey(data.head_names[i]) && bones.TryGetValue(data.head_names[i], out var t))
                {
                    src.Add(new Vector3(data.heads[3 * i], data.heads[3 * i + 1], data.heads[3 * i + 2]));
                    dst.Add(BindPose(t).inverse.GetColumn(3));
                }
            var fit = Fit(src, dst, false); var mirrored = Fit(src, dst, true);
            bool mirror = mirrored.error < fit.error;
            if (mirror) fit = mirrored;
            if (fit.error > .002f) throw new InvalidOperationException($"{dataPath}: bone heads fit {assetPath} only to {fit.error * 1000f:F1} mm");

            int n = data.positions.Length / 3;
            var vertices = new Vector3[n]; var normals = new Vector3[n]; var uv = new Vector2[n]; var weights = new BoneWeight[n];
            Vector3 M(Vector3 v) => mirror ? new Vector3(-v.x, v.y, v.z) : v;
            for (int i = 0; i < n; i++)
            {
                vertices[i] = fit.scale * (fit.rotation * M(new Vector3(data.positions[3 * i], data.positions[3 * i + 1], data.positions[3 * i + 2]))) + fit.translation;
                normals[i] = (fit.rotation * M(new Vector3(data.normals[3 * i], data.normals[3 * i + 1], data.normals[3 * i + 2]))).normalized;
                uv[i] = new Vector2(data.uv[2 * i], data.uv[2 * i + 1]);
                int k = 4 * i;
                weights[i] = new BoneWeight
                {
                    boneIndex0 = data.bone_index[k], weight0 = data.bone_weight[k], boneIndex1 = data.bone_index[k + 1], weight1 = data.bone_weight[k + 1],
                    boneIndex2 = data.bone_index[k + 2], weight2 = data.bone_weight[k + 2], boneIndex3 = data.bone_index[k + 3], weight3 = data.bone_weight[k + 3],
                };
            }
            var triangles = (int[])data.triangles.Clone();
            if (mirror) for (int i = 0; i < triangles.Length; i += 3) (triangles[i + 1], triangles[i + 2]) = (triangles[i + 2], triangles[i + 1]);
            var newBones = new Transform[data.bone_names.Length]; var bindposes = new Matrix4x4[newBones.Length];
            for (int i = 0; i < newBones.Length; i++)
            {
                if (!bones.TryGetValue(data.bone_names[i], out newBones[i])) throw new InvalidOperationException(assetPath + " has no bone " + data.bone_names[i]);
                bindposes[i] = BindPose(newBones[i]);
            }
            var mesh = new Mesh();
            if (n > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = vertices; mesh.normals = normals; mesh.uv = uv; mesh.triangles = triangles;
            mesh.boneWeights = weights; mesh.bindposes = bindposes;
            mesh.RecalculateTangents(); mesh.RecalculateBounds();
            return (mesh, newBones);
        }

        private struct Similarity { public Quaternion rotation; public Vector3 translation; public float scale, error; }

        /// <summary>Least-squares similarity dst ≈ s·R·src + t (Horn's quaternion method); mirror flips src's x first.</summary>
        private static Similarity Fit(List<Vector3> src, List<Vector3> dst, bool mirror)
        {
            var a = new List<Vector3>(src.Count);
            foreach (var v in src) a.Add(mirror ? new Vector3(-v.x, v.y, v.z) : v);
            Vector3 ca = Vector3.zero, cb = Vector3.zero;
            for (int i = 0; i < a.Count; i++) { ca += a[i]; cb += dst[i]; }
            ca /= a.Count; cb /= a.Count;
            double sa = 0, sb = 0; var S = new double[3, 3];
            for (int i = 0; i < a.Count; i++)
            {
                Vector3 p = a[i] - ca, q = dst[i] - cb;
                sa += p.sqrMagnitude; sb += q.sqrMagnitude;
                for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++) S[r, c] += p[r] * q[c];
            }
            double[,] N =
            {
                { S[0,0] + S[1,1] + S[2,2], S[1,2] - S[2,1], S[2,0] - S[0,2], S[0,1] - S[1,0] },
                { S[1,2] - S[2,1], S[0,0] - S[1,1] - S[2,2], S[0,1] + S[1,0], S[2,0] + S[0,2] },
                { S[2,0] - S[0,2], S[0,1] + S[1,0], -S[0,0] + S[1,1] - S[2,2], S[1,2] + S[2,1] },
                { S[0,1] - S[1,0], S[2,0] + S[0,2], S[1,2] + S[2,1], -S[0,0] - S[1,1] + S[2,2] },
            };
            // Largest eigenvector by power iteration on N shifted to be positive definite.
            double shift = 0; for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) shift += Math.Abs(N[r, c]);
            var e = new double[] { 1, .1, .1, .1 };
            for (int it = 0; it < 500; it++)
            {
                var x = new double[4];
                for (int r = 0; r < 4; r++) { x[r] = shift * e[r]; for (int c = 0; c < 4; c++) x[r] += N[r, c] * e[c]; }
                double len = Math.Sqrt(x[0] * x[0] + x[1] * x[1] + x[2] * x[2] + x[3] * x[3]);
                for (int r = 0; r < 4; r++) e[r] = x[r] / len;
            }
            var R = new Quaternion((float)e[1], (float)e[2], (float)e[3], (float)e[0]);
            float s = (float)Math.Sqrt(sb / Math.Max(1e-12, sa));
            Vector3 t = cb - s * (R * ca);
            float err = 0;
            for (int i = 0; i < a.Count; i++) err = Mathf.Max(err, (s * (R * a[i]) + t - dst[i]).magnitude);
            return new Similarity { rotation = R, translation = t, scale = s, error = err };
        }
    }
}
