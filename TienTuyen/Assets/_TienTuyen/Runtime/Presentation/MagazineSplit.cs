using System.Collections.Generic;
using UnityEngine;

namespace TienTuyen.Presentation
{
    /// <summary>
    /// Cuts a world gun's magazine out of its meshes: every island (triangles joined through shared vertices) lying wholly
    /// inside the box. The rest stays on the gun; the magazine goes to
    /// a "Magazine" child of a <see cref="MountName"/> at the gun root, which the animator moves through a reload. The
    /// split meshes are made once per source mesh.
    /// </summary>
    public static class MagazineSplit
    {
        public const string MountName = "MagazineMount";

        private static readonly Dictionary<(Mesh, Bounds), (Mesh body, Mesh magazine, int triangles)> cache =
            new Dictionary<(Mesh, Bounds), (Mesh, Mesh, int)>();

        /// <summary>Splits the gun instance under <paramref name="gun"/>; returns the magazine's triangle count (0: nothing in the box).</summary>
        /// <param name="box">The magazine's bounds in the gun root's space.</param>
        public static int Apply(Transform gun, Bounds box)
        {
            int total = 0;
            Transform mount = null;
            foreach (var renderer in gun.GetComponentsInChildren<MeshRenderer>(true))
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null || renderer.name == "Magazine") continue;
                Matrix4x4 toGun = gun.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                var split = Split(filter.sharedMesh, toGun, box);
                if (split.triangles == 0) continue;
                total += split.triangles;
                filter.sharedMesh = split.body;
                if (mount == null)
                {
                    mount = new GameObject(MountName).transform;
                    mount.SetParent(gun, false);
                }
                var magazine = new GameObject("Magazine", typeof(MeshFilter), typeof(MeshRenderer));
                magazine.transform.SetParent(mount, false);
                magazine.transform.localPosition = toGun.GetColumn(3);
                magazine.transform.localRotation = toGun.rotation;
                magazine.transform.localScale = toGun.lossyScale;
                magazine.GetComponent<MeshFilter>().sharedMesh = split.magazine;
                var drawn = magazine.GetComponent<MeshRenderer>();
                drawn.sharedMaterials = renderer.sharedMaterials;
                drawn.shadowCastingMode = renderer.shadowCastingMode;
            }
            return total;
        }

        /// <summary>The magazine's triangle count in a gun model (asset or instance), without changing it.</summary>
        public static int Triangles(GameObject model, Bounds box)
        {
            int total = 0;
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null)
                    total += Classify(filter.sharedMesh, model.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix, box).count;
            return total;
        }

        private static (Mesh body, Mesh magazine, int triangles) Split(Mesh mesh, Matrix4x4 toGun, Bounds box)
        {
            if (cache.TryGetValue((mesh, box), out var cached)) return cached;
            var (submeshes, cut, count) = Classify(mesh, toGun, box);
            if (count == 0) return cache[(mesh, box)] = (null, null, 0);
            var body = Object.Instantiate(mesh);
            var magazine = Object.Instantiate(mesh);
            body.name = mesh.name + "_NoMagazine";
            magazine.name = mesh.name + "_Magazine";
            for (int s = 0; s < submeshes.Length; s++)
            {
                var kept = new List<int>();
                var cutOut = new List<int>();
                var t = submeshes[s];
                for (int i = 0; i < t.Length; i += 3)
                {
                    var into = cut[s][i / 3] ? cutOut : kept;
                    into.Add(t[i]); into.Add(t[i + 1]); into.Add(t[i + 2]);
                }
                body.SetTriangles(kept, s);
                magazine.SetTriangles(cutOut, s);
            }
            return cache[(mesh, box)] = (body, magazine, count);
        }

        /// <summary>
        /// Per submesh triangle: whether its island lies wholly inside the box (in the gun's space through
        /// <paramref name="toGun"/>). Triangles join an island through shared vertex indices, the mesh's own connectivity:
        /// the magazines touch their wells along coincident edges, so joining by position would fuse them to the gun.
        /// </summary>
        private static (int[][] submeshes, bool[][] cut, int count) Classify(Mesh mesh, Matrix4x4 toGun, Bounds box)
        {
            var vertices = mesh.vertices;
            var parent = new int[vertices.Length];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
            var submeshes = new int[mesh.subMeshCount][];
            for (int s = 0; s < submeshes.Length; s++)
            {
                var t = submeshes[s] = mesh.GetTriangles(s);
                for (int i = 0; i < t.Length; i += 3)
                {
                    int a = Find(t[i]);
                    parent[Find(t[i + 1])] = a;
                    parent[Find(t[i + 2])] = a;
                }
            }
            // An island is out as soon as one of its vertices is.
            var outside = new HashSet<int>();
            for (int i = 0; i < vertices.Length; i++)
                if (!box.Contains(toGun.MultiplyPoint3x4(vertices[i]))) outside.Add(Find(i));
            var cut = new bool[submeshes.Length][];
            int count = 0;
            for (int s = 0; s < submeshes.Length; s++)
            {
                cut[s] = new bool[submeshes[s].Length / 3];
                for (int i = 0; i < cut[s].Length; i++)
                    if (cut[s][i] = !outside.Contains(Find(submeshes[s][i * 3]))) count++;
            }
            return (submeshes, cut, count);
        }
    }
}
