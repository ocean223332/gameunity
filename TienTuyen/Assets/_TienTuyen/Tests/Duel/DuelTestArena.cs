using System;
using System.Collections.Generic;
using UnityEngine;

namespace TienTuyen.Duel.Tests
{
    internal static class DuelTestArena
    {
        internal const float GridStep = .5f;

        // Find a real clear firing lane; combat fixtures must not accidentally pass because a rebuilt map blocked them.
        internal static void ClearPair(float distance, out Vector3 from, out Vector3 to)
        {
            for (float z = -DuelArenaLayout.HalfHeight + 2; z <= DuelArenaLayout.HalfHeight - 2; z += GridStep)
            for (float x = -DuelArenaLayout.HalfWidth + 2; x + distance <= DuelArenaLayout.HalfWidth - 2; x += GridStep)
            {
                from = new Vector3(x, DuelArenaLayout.GroundY, z);
                to = from + Vector3.right * distance;
                if (ClearBody(from) && ClearBody(to) &&
                    DuelArenaLayout.HasLineOfSight(from, to) &&
                    DuelArenaLayout.HasLineOfSight(from + Vector3.up, to + Vector3.up)) return;
            }
            throw new InvalidOperationException("No clear in-bounds combat fixture of the required range.");
        }

        internal static void FiringPositions(out Vector2 slot0, out Vector2 slot1)
        {
            float halfGap = Mathf.Min(2.5f, DuelPlayerSim.WeaponAt(0).Range / 3);
            for (float z = -DuelArenaLayout.HalfHeight + 1; z <= DuelArenaLayout.HalfHeight - 1; z += GridStep)
            {
                var from = new Vector3(-halfGap, DuelArenaLayout.GroundY, z);
                var to = new Vector3(halfGap, DuelArenaLayout.GroundY, z);
                if (!ClearBody(from) || !ClearBody(to) || !DuelArenaLayout.HasLineOfSight(from, to)) continue;
                slot0 = new Vector2(from.x, from.z); slot1 = new Vector2(to.x, to.z); return;
            }
            throw new InvalidOperationException("No mirrored clear duel firing positions.");
        }

        // Route only the test driver; gameplay movement still runs through the real server loop and cover solver.
        internal static Vector2[] Route(Vector3 from, Vector2 destination)
        {
            float minX = -DuelArenaLayout.HalfWidth + 1, minZ = -DuelArenaLayout.HalfHeight + 1;
            int width = Mathf.RoundToInt((DuelArenaLayout.HalfWidth * 2 - 2) / GridStep) + 1;
            int height = Mathf.RoundToInt((DuelArenaLayout.HalfHeight * 2 - 2) / GridStep) + 1;
            int start = Index(from.x, from.z, minX, minZ, width);
            int goal = Index(destination.x, destination.y, minX, minZ, width);
            var parent = new int[width * height];
            for (int i = 0; i < parent.Length; i++) parent[i] = -1;
            var queue = new Queue<int>();
            queue.Enqueue(start); parent[start] = start;
            while (queue.Count > 0 && parent[goal] < 0)
            {
                int cell = queue.Dequeue(), x = cell % width, z = cell / width;
                Visit(x - 1, z, cell, width, height, minX, minZ, parent, queue);
                Visit(x + 1, z, cell, width, height, minX, minZ, parent, queue);
                Visit(x, z - 1, cell, width, height, minX, minZ, parent, queue);
                Visit(x, z + 1, cell, width, height, minX, minZ, parent, queue);
            }
            if (parent[goal] < 0) throw new InvalidOperationException("No walkable route from spawn to the firing fixture.");
            var path = new List<Vector2>();
            for (int cell = goal; cell != start; cell = parent[cell])
                path.Add(new Vector2(minX + cell % width * GridStep, minZ + cell / width * GridStep));
            path.Reverse();
            return path.ToArray();
        }

        private static void Visit(int x, int z, int previous, int width, int height, float minX, float minZ,
            int[] parent, Queue<int> queue)
        {
            if (x < 0 || z < 0 || x >= width || z >= height) return;
            int cell = z * width + x;
            if (parent[cell] >= 0 || !ClearBody(new Vector3(minX + x * GridStep, DuelArenaLayout.GroundY,
                minZ + z * GridStep))) return;
            parent[cell] = previous;
            queue.Enqueue(cell);
        }

        private static int Index(float x, float z, float minX, float minZ, int width) =>
            Mathf.RoundToInt((z - minZ) / GridStep) * width + Mathf.RoundToInt((x - minX) / GridStep);

        private static bool ClearBody(Vector3 point) => !DuelArenaLayout.IsInsideCover(new Vector2(point.x, point.z),
            DuelArenaLayout.PlayerRadius + .001f);
    }
}
