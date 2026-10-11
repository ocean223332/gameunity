using System;
using System.Collections.Generic;
using UnityEngine;

namespace TienTuyen.Presentation
{
    /// <summary>Bounded, collider-free death visuals independent of the gameplay enemy pool.</summary>
    public sealed class CombatCorpsePool : MonoBehaviour
    {
        private const int Capacity = 8;
        private sealed class Corpse
        {
            public CombatCharacterRig rig;
            public Vector3 facing;
            public float age, lifetime;
        }
        private readonly List<Corpse> slots = new List<Corpse>(Capacity);

        public int ActiveCount
        {
            get
            {
                int count = 0;
                foreach (var slot in slots) if (slot.rig != null && slot.rig.gameObject.activeSelf) count++;
                return count;
            }
        }

        public void Spawn(SkinnedCharacterProfile profile, Vector3 position, Vector3 facing)
        {
            if (profile == null || profile.death == null) return;
            Corpse corpse = null;
            foreach (var slot in slots)
                if (!slot.rig.gameObject.activeSelf && slot.rig.Profile == profile) { corpse = slot; break; }
            if (corpse == null && slots.Count < Capacity) corpse = Create(profile);
            if (corpse == null)
            {
                // Recycle the oldest compatible body, rather than grow without bound.
                foreach (var slot in slots)
                    if (slot.rig.Profile == profile && (corpse == null || slot.age > corpse.age)) corpse = slot;
            }
            if (corpse == null && slots.Count > 0)
            {
                // Full of other enemy types: rebuild the oldest (preferably hidden) body for this one.
                Corpse oldest = null;
                foreach (var slot in slots)
                {
                    bool hidden = !slot.rig.gameObject.activeSelf, oldestHidden = oldest != null && !oldest.rig.gameObject.activeSelf;
                    if (oldest == null || hidden && !oldestHidden || hidden == oldestHidden && slot.age > oldest.age) oldest = slot;
                }
                slots.Remove(oldest);
                Destroy(oldest.rig.gameObject);
                corpse = Create(profile);
            }
            if (corpse == null) return;
            corpse.rig.gameObject.SetActive(true);
            corpse.rig.transform.position = position;
            corpse.rig.transform.rotation = Quaternion.identity;
            corpse.rig.ResetPose();
            corpse.facing = facing.sqrMagnitude > .0001f ? facing.normalized : Vector3.forward;
            corpse.age = 0;
            corpse.lifetime = Mathf.Max(.5f, profile.death.length) + 1.5f;
            corpse.rig.Tick(0f, Vector3.zero, corpse.facing, -1f, 0f, 1f);
        }

        private Corpse Create(SkinnedCharacterProfile profile)
        {
            var node = new GameObject("EnemyDeathVisual");
            node.transform.SetParent(transform, false);
            var rig = node.AddComponent<CombatCharacterRig>();
            if (!rig.InitializeSkinned(profile, Array.Empty<Transform>(), new Vector3(0, -.8f, 0)))
            { Destroy(node); return null; }
            var corpse = new Corpse { rig = rig };
            slots.Add(corpse);
            return corpse;
        }

        public void Tick(float dt)
        {
            if (dt <= 0) return;
            foreach (var slot in slots)
            {
                if (!slot.rig.gameObject.activeSelf) continue;
                slot.age += dt;
                if (slot.age >= slot.lifetime) { slot.rig.gameObject.SetActive(false); continue; }
                slot.rig.Tick(dt, Vector3.zero, slot.facing, -1f, 0f, 1f);
            }
        }

        public void Clear()
        {
            foreach (var slot in slots) if (slot.rig != null) slot.rig.gameObject.SetActive(false);
        }

        private void OnDisable() => Clear();
    }
}
