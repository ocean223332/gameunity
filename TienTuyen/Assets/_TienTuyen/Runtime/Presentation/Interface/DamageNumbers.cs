using TMPro;
using TienTuyen.Combat;
using UnityEngine;

namespace TienTuyen.Presentation.Interface
{
    /// <summary>Pooled screen-space labels anchored to a snapshot of the world hit position.</summary>
    public sealed class DamageNumbers : MonoBehaviour
    {
        private const int Capacity = 48;
        private const float Lifetime = .85f;
        private sealed class Entry
        {
            public TMP_Text Label;
            public Vector3 World;
            public Vector2 Offset;
            public float Age;
            public bool Active;
        }
        private readonly Entry[] entries = new Entry[Capacity];
        private CombatGame game;
        private RectTransform root;
        private Camera view;
        private int next;

        public void Initialize(CombatGame combat, Transform parent)
        {
            game = combat;
            root = UiKit.Rect("DamageNumbers", parent);
            for (int i = 0; i < Capacity; i++)
            {
                var label = UiKit.Text("Hit" + i, root, "", UiTheme.Display, 27, UiTheme.Paper,
                    TextAlignmentOptions.Center);
                UiKit.Place(label.rectTransform, UiKit.Center, UiKit.Center, Vector2.zero, new Vector2(220, 68));
                label.outlineWidth = .18f;
                label.outlineColor = new Color32(10, 15, 10, 255);
                label.gameObject.SetActive(false);
                entries[i] = new Entry { Label = label };
            }
            game.DamageFeedback += OnDamage;
            game.TransientsCleared += Clear;
        }

        private void OnDamage(Vector3 position, float damage, bool headshot, bool critical)
        {
            if (game.State != CombatState.Playing || damage <= 0) return;
            var entry = entries[next];
            next = (next + 1) % Capacity;
            entry.Active = true;
            entry.Age = 0;
            entry.World = position;
            entry.Offset = new Vector2((next % 5 - 2) * 16f, 30f + next % 3 * 14f);
            entry.Label.color = headshot ? UiTheme.GoldBright : critical ? UiTheme.Danger : UiTheme.Paper;
            entry.Label.fontSize = headshot ? 32 : 27;
            entry.Label.text = $"−{damage:0.#}" + (headshot ? "\n<size=12>HEADSHOT</size>" : critical ? "\n<size=12>CRITICAL</size>" : "");
            entry.Label.gameObject.SetActive(true);
        }

        private void LateUpdate()
        {
            if (game == null) return;
            bool visible = game.State == CombatState.Playing && !game.CommanderCinematic;
            root.gameObject.SetActive(visible);
            if (!visible) return;
            if (view == null) view = Camera.main;
            if (view == null) return;
            foreach (var entry in entries)
            {
                if (!entry.Active) continue;
                entry.Age += Time.deltaTime;
                if (entry.Age >= Lifetime)
                {
                    entry.Active = false;
                    entry.Label.gameObject.SetActive(false);
                    continue;
                }
                Vector3 screen = view.WorldToScreenPoint(entry.World);
                bool onScreen = screen.z > 0 && screen.x >= 0 && screen.x <= Screen.width && screen.y >= 0 && screen.y <= Screen.height;
                entry.Label.gameObject.SetActive(onScreen);
                if (!onScreen) continue;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out Vector2 local);
                float t = entry.Age / Lifetime;
                entry.Label.rectTransform.anchoredPosition = local + entry.Offset + Vector2.up * (t * 65f);
                entry.Label.rectTransform.localScale = Vector3.one * (1f + .15f * Mathf.Max(0, 1f - t * 5f));
                entry.Label.alpha = 1f - Mathf.InverseLerp(.5f, 1f, t);
            }
        }

        private void Clear()
        {
            foreach (var entry in entries)
            {
                entry.Active = false;
                entry.Label.gameObject.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (game != null)
            {
                game.DamageFeedback -= OnDamage;
                game.TransientsCleared -= Clear;
            }
            if (root != null) Destroy(root.gameObject);
        }
    }
}
