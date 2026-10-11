using System;
using UnityEngine;

namespace TienTuyen.Presentation
{
    /// <summary>
    /// The plant species FloraScatter may place, each a prefab with a LODGroup (LOD0, LOD1 and a cross-card)
    /// built by the Editor's FloraPrefabBuilder from Art/Flora. Where each species grows is decided by
    /// EnvironmentFlora; the biome only says what exists and how big it is. Loaded from Resources.
    /// </summary>
    [CreateAssetMenu(menuName = "Tien Tuyen/Flora Biome")]
    public sealed class FloraBiome : ScriptableObject
    {
        public const string ResourcePath = "Environment/FloraBiome";

        [Serializable]
        public sealed class Species
        {
            public string Name;
            public GameObject Prefab;
            /// <summary>Height of the prefab at scale 1 (m).</summary>
            public float Height;
            /// <summary>Crown or leaf spread from the stem at scale 1 (m).</summary>
            public float Radius;
        }

        /// <summary>Plants taller than this block the shoulder camera, so they stay out of the edge band.</summary>
        public const float TallHeight = 1.8f;

        public int Seed = 1972;
        public Species[] Plants = Array.Empty<Species>();

        public int IndexOf(string name)
        {
            for (int i = 0; i < Plants.Length; i++)
                if (Plants[i].Name == name) return i;
            return -1;
        }
    }
}
