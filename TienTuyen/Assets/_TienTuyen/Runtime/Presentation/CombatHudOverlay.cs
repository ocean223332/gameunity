using System.Collections.Generic;
using TMPro;
using TienTuyen.Combat;
using UnityEngine;
using UnityEngine.UI;

namespace TienTuyen.Presentation
{
    /// <summary>
    /// Third-person combat read-outs drawn on the HUD canvas: crosshair, hit
    /// markers, floating damage, enemy health bars, damage direction and radar.
    /// </summary>
    public sealed class CombatHudOverlay : MonoBehaviour
    {
        private const int DamageLabels = 24, RadarDots = 40;
        private const float RadarRange = 22f, RadarRadius = 62f;
        private static readonly Color Paper = new Color(.93f, .91f, .83f), Danger = new Color(.95f, .32f, .22f), Gold = new Color(.91f, .74f, .38f);

        private CombatGame game;
        private RectTransform root, crosshair, hitMarker, radar, radarDots;
        private readonly Image[] ticks = new Image[4];
        private Image dot, vignette;
        private TMP_Text ammo;
        private readonly Image[] hitBars = new Image[4];
        private float hitTime, bloom, vignettePulse;
        private bool hitKill;
        private CombatThirdPersonCamera view;
        private Transform player, supply;
        private readonly List<Transform> enemies = new List<Transform>();
        private readonly List<Transform> pickups = new List<Transform>();

        private sealed class Floating { public TMP_Text label; public Vector3 world; public float age = 99f; public float drift; }
        private readonly Floating[] floating = new Floating[DamageLabels];
        private int nextFloating;

        private sealed class Bar { public RectTransform root; public Image fill; public float shown; }
        private readonly Dictionary<Transform, Bar> bars = new Dictionary<Transform, Bar>();

        private sealed class Arrow { public RectTransform root; public Image image; public Vector3 source; public float age = 99f; }
        private readonly Arrow[] arrows = new Arrow[4];
        private int nextArrow;
        private readonly Image[] radarImages = new Image[RadarDots];
        private readonly System.Random random = new System.Random(77);

        public void Initialize(CombatGame combat, RectTransform hud, TMP_FontAsset font)
        {
            game = combat;
            root = hud;
            player = combat.transform.Find("Player");
            supply = combat.transform.Find("SupplyCrate");
            foreach (Transform child in combat.transform)
            {
                if (child.name.StartsWith("Enemy_", System.StringComparison.Ordinal)) enemies.Add(child);
                else if (child.name.StartsWith("Pickup_", System.StringComparison.Ordinal)) pickups.Add(child);
            }

            vignette = Graphic<Image>("DamageVignette", root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            vignette.sprite = Sprite.Create(VignetteTexture(), new Rect(0, 0, 256, 256), new Vector2(.5f, .5f));
            vignette.color = new Color(.75f, .05f, .03f, 0);
            vignette.transform.SetAsFirstSibling();

            var bars = Rect("EnemyHealthBars", root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            foreach (var enemy in enemies)
            {
                var back = Graphic<Image>("Bar", bars, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(-24, -3.5f), new Vector2(24, 3.5f));
                back.color = new Color(.08f, .09f, .07f, .75f);
                var fill = Graphic<Image>("Fill", back.rectTransform, Vector2.zero, Vector2.one, new Vector2(1, 1), new Vector2(-1, -1));
                fill.color = Danger;
                fill.type = Image.Type.Filled;
                fill.sprite = WhiteSprite();
                fill.fillMethod = Image.FillMethod.Horizontal;
                back.gameObject.SetActive(false);
                this.bars.Add(enemy, new Bar { root = back.rectTransform, fill = fill });
            }

            var numbers = Rect("DamageNumbers", root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            for (int i = 0; i < floating.Length; i++)
            {
                var label = Graphic<TextMeshProUGUI>("Damage", numbers, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(-60, -20), new Vector2(60, 20));
                label.font = font;
                label.fontSize = 22;
                label.alignment = TextAlignmentOptions.Center;
                label.outlineWidth = .22f;
                label.outlineColor = new Color32(20, 16, 12, 255);
                label.gameObject.SetActive(false);
                floating[i] = new Floating { label = label };
            }

            for (int i = 0; i < arrows.Length; i++)
            {
                var arrow = Graphic<Image>("DamageDirection", root, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(-130, -130), new Vector2(130, 130));
                arrow.sprite = Sprite.Create(ArcTexture(), new Rect(0, 0, 128, 128), new Vector2(.5f, .5f));
                arrow.color = new Color(1, .25f, .15f, 0);
                arrows[i] = new Arrow { root = arrow.rectTransform, image = arrow };
            }

            crosshair = Rect("Crosshair", root, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(-40, -40), new Vector2(40, 40));
            for (int i = 0; i < 4; i++)
            {
                ticks[i] = Graphic<Image>("Tick", crosshair, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
                ticks[i].sprite = WhiteSprite();
                var shadow = ticks[i].gameObject.AddComponent<Outline>();
                shadow.effectColor = new Color(0, 0, 0, .55f);
                shadow.effectDistance = new Vector2(1, -1);
            }
            dot = Graphic<Image>("Dot", crosshair, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(-2, -2), new Vector2(2, 2));
            dot.sprite = WhiteSprite();
            hitMarker = Rect("HitMarker", crosshair, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(-40, -40), new Vector2(40, 40));
            for (int i = 0; i < 4; i++)
            {
                hitBars[i] = Graphic<Image>("Hit", hitMarker, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
                hitBars[i].sprite = WhiteSprite();
                // Pivot below the bar so each arm sits 8 px out from the centre and rotates about it.
                hitBars[i].rectTransform.sizeDelta = new Vector2(3f, 10f);
                hitBars[i].rectTransform.pivot = new Vector2(.5f, -.8f);
                hitBars[i].rectTransform.anchoredPosition = Vector2.zero;
                hitBars[i].rectTransform.localRotation = Quaternion.Euler(0, 0, 45 + i * 90);
            }
            ammo = Graphic<TextMeshProUGUI>("CrosshairAmmo", crosshair, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(24, -46), new Vector2(150, -20));
            ammo.font = font;
            ammo.fontSize = 16;
            ammo.alignment = TextAlignmentOptions.MidlineLeft;
            ammo.color = new Color(Paper.r, Paper.g, Paper.b, .85f);
            ammo.outlineWidth = .2f;
            ammo.outlineColor = new Color32(0, 0, 0, 200);

            radar = Rect("Radar", root, Vector2.one, Vector2.one, new Vector2(-30 - RadarRadius * 2, -22 - RadarRadius * 2), new Vector2(-30, -22));
            var disc = Graphic<Image>("Disc", radar, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            disc.sprite = Interface.UiSprites.Disc;
            disc.color = new Color(.05f, .08f, .06f, .62f);
            var ring = Graphic<Image>("Ring", radar, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            ring.sprite = Interface.UiSprites.Ring(.025f);
            ring.color = new Color(Paper.r, Paper.g, Paper.b, .35f);
            var inner = Graphic<Image>("InnerRing", radar, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(-RadarRadius * .5f, -RadarRadius * .5f), new Vector2(RadarRadius * .5f, RadarRadius * .5f));
            inner.sprite = Interface.UiSprites.Ring(.04f);
            inner.color = new Color(Paper.r, Paper.g, Paper.b, .14f);
            for (int i = 0; i < 4; i++)
            {
                var tick = Graphic<Image>("Tick", radar, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
                tick.sprite = WhiteSprite();
                tick.color = new Color(Paper.r, Paper.g, Paper.b, .3f);
                tick.rectTransform.sizeDelta = i < 2 ? new Vector2(1.5f, 7f) : new Vector2(7f, 1.5f);
                tick.rectTransform.anchoredPosition = new[] { Vector2.up, Vector2.down, Vector2.left, Vector2.right }[i] * (RadarRadius - 6f);
            }
            var cone = Graphic<Image>("ViewCone", radar, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(-RadarRadius, -RadarRadius), new Vector2(RadarRadius, RadarRadius));
            cone.sprite = Sprite.Create(ConeTexture(), new Rect(0, 0, 128, 128), new Vector2(.5f, .5f));
            cone.color = new Color(Paper.r, Paper.g, Paper.b, .16f);
            radarDots = Rect("Dots", radar, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var circle = Sprite.Create(CircleTexture(32, 0f), new Rect(0, 0, 32, 32), new Vector2(.5f, .5f));
            for (int i = 0; i < radarImages.Length; i++)
            {
                radarImages[i] = Graphic<Image>("Blip", radarDots, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(-3, -3), new Vector2(3, 3));
                radarImages[i].sprite = circle;
                radarImages[i].gameObject.SetActive(false);
            }
            var self = Graphic<Image>("Self", radar, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(-5, -6), new Vector2(5, 6));
            self.sprite = Sprite.Create(TriangleTexture(), new Rect(0, 0, 32, 32), new Vector2(.5f, .5f));
            self.color = new Color(.72f, .9f, .55f);

            game.EnemyDamaged += OnEnemyDamaged;
            game.PlayerDamaged += OnPlayerDamaged;
            game.WeaponFired += OnWeaponFired;
        }

        private void OnDestroy()
        {
            if (game == null) return;
            game.EnemyDamaged -= OnEnemyDamaged;
            game.PlayerDamaged -= OnPlayerDamaged;
            game.WeaponFired -= OnWeaponFired;
        }

        private void OnWeaponFired(int slot, Vector3 start, Vector3 end) => bloom = Mathf.Min(bloom + 6f, 16f);

        private void OnEnemyDamaged(Transform actor, float damage, bool critical, bool killed)
        {
            hitTime = killed ? .28f : .14f;
            hitKill = killed;
            if (bars.TryGetValue(actor, out var bar)) bar.shown = 3f;
            var entry = floating[nextFloating];
            nextFloating = (nextFloating + 1) % floating.Length;
            entry.age = 0;
            entry.world = actor.position + Vector3.up * (1.05f + (float)random.NextDouble() * .25f);
            entry.drift = ((float)random.NextDouble() - .5f) * 30f;
            entry.label.text = critical ? Mathf.RoundToInt(damage) + "!" : Mathf.RoundToInt(damage).ToString();
            entry.label.color = critical ? Gold : killed ? new Color(1f, .6f, .45f) : Paper;
            entry.label.fontSize = critical ? 28 : 20;
            entry.label.gameObject.SetActive(true);
        }

        private void OnPlayerDamaged(Vector3 source, float damage)
        {
            vignettePulse = Mathf.Min(1f, vignettePulse + .55f);
            if (player == null) return;
            Vector3 offset = source - player.position;
            offset.y = 0;
            if (offset.sqrMagnitude < .01f) return;
            var arrow = arrows[nextArrow];
            nextArrow = (nextArrow + 1) % arrows.Length;
            arrow.source = source;
            arrow.age = 0;
        }

        private void LateUpdate()
        {
            if (game == null) return;
            if (view == null && Camera.main != null) view = Camera.main.GetComponent<CombatThirdPersonCamera>();
            bool playing = game.State == CombatState.Playing;
            crosshair.gameObject.SetActive(playing && game.ManualAim);
            // Pause freezes every read-out exactly where it was.
            if (game.State == CombatState.Paused) return;
            bool combat = playing;
            float dt = Time.unscaledDeltaTime;
            radar.gameObject.SetActive(combat);
            UpdateCrosshair(dt);
            UpdateFloating(dt, combat);
            UpdateBars(dt, combat);
            UpdateArrows(dt, combat);
            UpdateRadar();
            float lowHealth = combat && game.MaxHealth > 0 ? Mathf.Clamp01(1f - game.Health / (game.MaxHealth * .35f)) : 0f;
            vignettePulse = Mathf.Max(0, vignettePulse - dt * 1.6f);
            vignette.color = new Color(.75f, .05f, .03f, Mathf.Clamp01(vignettePulse * .8f + lowHealth * (.28f + Mathf.Sin(Time.unscaledTime * 5f) * .08f)));
        }

        private void UpdateCrosshair(float dt)
        {
            if (!crosshair.gameObject.activeSelf) return;
            bloom = Mathf.Max(0, bloom - dt * 40f);
            float aim = view != null ? view.AimBlend : 0f;
            float gap = Mathf.Lerp(9f, 5f, aim) + bloom;
            bool onTarget = game.AimOnTarget || (view != null && view.AimingAtEnemy);
            Color color = onTarget ? Danger : Paper;
            Vector2[] directions = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };
            for (int i = 0; i < 4; i++)
            {
                bool vertical = i < 2;
                var rect = ticks[i].rectTransform;
                rect.sizeDelta = vertical ? new Vector2(2.5f, 9f) : new Vector2(9f, 2.5f);
                rect.anchoredPosition = directions[i] * (gap + 4.5f);
                ticks[i].color = color;
            }
            dot.color = color;
            hitTime = Mathf.Max(0, hitTime - dt);
            hitMarker.gameObject.SetActive(hitTime > 0);
            foreach (var bar in hitBars) bar.color = hitKill ? Danger : Color.white;
            hitMarker.localScale = Vector3.one * (1f + hitTime * 1.6f);
            float reload = game.ReloadRemaining;
            ammo.text = reload > 0 ? "NẠP " + reload.ToString("0.0") : game.Ammo + " / " + game.MagazineSize;
            ammo.color = reload > 0 || game.Ammo <= Mathf.Max(1, game.MagazineSize / 4) ? Gold : new Color(Paper.r, Paper.g, Paper.b, .85f);
        }

        private void UpdateFloating(float dt, bool combat)
        {
            var camera = Camera.main;
            foreach (var entry in floating)
            {
                if (entry.age > .9f || !combat || camera == null)
                {
                    if (entry.label.gameObject.activeSelf && (entry.age > .9f || !combat)) entry.label.gameObject.SetActive(false);
                    continue;
                }
                entry.age += dt;
                Vector3 world = entry.world + Vector3.up * (entry.age * 1.1f);
                if (!Project(camera, world, out Vector2 local)) { entry.label.gameObject.SetActive(false); continue; }
                entry.label.gameObject.SetActive(true);
                entry.label.rectTransform.anchoredPosition = local + new Vector2(entry.drift * entry.age, 0);
                float pop = entry.age < .08f ? 1.35f - entry.age * 4f : 1f;
                entry.label.rectTransform.localScale = Vector3.one * pop;
                var c = entry.label.color;
                c.a = Mathf.Clamp01(1.4f - entry.age * 1.6f);
                entry.label.color = c;
            }
        }

        private void UpdateBars(float dt, bool combat)
        {
            var camera = Camera.main;
            foreach (var pair in bars)
            {
                Transform actor = pair.Key;
                Bar bar = pair.Value;
                bar.shown = Mathf.Max(0, bar.shown - dt);
                bool visible = combat && camera != null && actor.gameObject.activeSelf && bar.shown > 0;
                Vector2 local = Vector2.zero;
                if (visible)
                {
                    float height = 1.2f * Mathf.Max(1f, actor.localScale.y / .75f);
                    visible = Project(camera, actor.position + Vector3.up * height, out local) &&
                              (actor.position - camera.transform.position).sqrMagnitude < 40f * 40f;
                }
                if (bar.root.gameObject.activeSelf != visible) bar.root.gameObject.SetActive(visible);
                if (!visible) continue;
                bar.root.anchoredPosition = local;
                bar.fill.fillAmount = game.EnemyHealthFraction(actor);
            }
        }

        private void UpdateArrows(float dt, bool combat)
        {
            foreach (var arrow in arrows)
            {
                arrow.age += dt;
                float alpha = combat ? Mathf.Clamp01(1.2f - arrow.age * 1.1f) : 0f;
                arrow.image.color = new Color(1, .25f, .15f, alpha * .85f);
                if (alpha <= 0 || player == null) continue;
                Vector3 offset = arrow.source - player.position;
                float yaw = view != null ? view.Yaw : 0f;
                float angle = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg - yaw;
                arrow.root.localRotation = Quaternion.Euler(0, 0, -angle);
            }
        }

        private void UpdateRadar()
        {
            if (!radar.gameObject.activeSelf || player == null) return;
            float yaw = (view != null ? view.Yaw : 0f) * Mathf.Deg2Rad;
            float sin = Mathf.Sin(yaw), cos = Mathf.Cos(yaw);
            int used = 0;
            void Blip(Vector3 world, Color color, float size)
            {
                if (used >= radarImages.Length) return;
                float dx = world.x - player.position.x, dz = world.z - player.position.z;
                // Rotate into view space: up on the radar is where the camera looks.
                var p = new Vector2(dx * cos - dz * sin, dx * sin + dz * cos) / RadarRange * RadarRadius;
                if (p.magnitude > RadarRadius - 3) { p = p.normalized * (RadarRadius - 3); size *= .8f; color.a *= .7f; }
                var image = radarImages[used++];
                image.gameObject.SetActive(true);
                image.rectTransform.anchoredPosition = p;
                image.rectTransform.sizeDelta = Vector2.one * size;
                image.color = color;
            }
            foreach (var enemy in enemies)
                if (enemy.gameObject.activeSelf)
                    Blip(enemy.position, enemy.localScale.x > .85f ? new Color(1f, .55f, .2f) : Danger, enemy.localScale.x > .85f ? 9f : 6f);
            foreach (var pickup in pickups)
                if (pickup.gameObject.activeSelf) Blip(pickup.position, new Color(Gold.r, Gold.g, Gold.b, .8f), 3.5f);
            if (supply != null && supply.gameObject.activeSelf) Blip(supply.position, Gold, 10f);
            for (int i = used; i < radarImages.Length; i++)
                if (radarImages[i].gameObject.activeSelf) radarImages[i].gameObject.SetActive(false);
        }

        private bool Project(Camera camera, Vector3 world, out Vector2 local)
        {
            Vector3 screen = camera.WorldToScreenPoint(world);
            local = Vector2.zero;
            if (screen.z <= .1f) return false;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out local);
        }

        // ---------- UI construction ----------

        private static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 min, Vector2 max)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(.5f, .5f);
            rect.offsetMin = min;
            rect.offsetMax = max;
            return rect;
        }

        private static T Graphic<T>(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 min, Vector2 max) where T : Graphic
        {
            var graphic = Rect(name, parent, anchorMin, anchorMax, min, max).gameObject.AddComponent<T>();
            graphic.raycastTarget = false;
            return graphic;
        }

        private static Sprite white;
        private static Sprite WhiteSprite()
        {
            if (white != null) return white;
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var pixels = new Color32[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
            texture.SetPixels32(pixels);
            texture.Apply();
            white = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(.5f, .5f));
            return white;
        }

        private static Texture2D Paint(int size, System.Func<float, float, float> alpha)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + .5f) / size * 2 - 1, v = (y + .5f) / size * 2 - 1;
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha(u, v)) * 255));
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>Filled disc when inner is 0, otherwise a ring from inner to the rim.</summary>
        private static Texture2D CircleTexture(int size, float inner) => Paint(size, (u, v) =>
        {
            float r = Mathf.Sqrt(u * u + v * v), edge = 2f / size;
            float outer = Mathf.Clamp01((1f - r) / edge);
            return inner <= 0 ? outer : outer * Mathf.Clamp01((r - inner) / edge);
        });

        private static Texture2D VignetteTexture() => Paint(256, (u, v) =>
        {
            float r = Mathf.Sqrt(u * u * .8f + v * v * .8f);
            return Mathf.SmoothStep(.55f, 1.15f, r);
        });

        private static Texture2D ArcTexture() => Paint(128, (u, v) =>
        {
            float r = Mathf.Sqrt(u * u + v * v);
            float angle = Mathf.Abs(Mathf.Atan2(u, v)) * Mathf.Rad2Deg;
            float band = Mathf.Clamp01(1f - Mathf.Abs(r - .88f) / .07f);
            return band * Mathf.Clamp01((32f - angle) / 10f);
        });

        private static Texture2D ConeTexture() => Paint(128, (u, v) =>
        {
            float r = Mathf.Sqrt(u * u + v * v);
            float angle = Mathf.Abs(Mathf.Atan2(u, v)) * Mathf.Rad2Deg;
            return r < 1f && v > 0 && angle < 32f ? (1f - r) * 1.2f : 0f;
        });

        private static Texture2D TriangleTexture() => Paint(32, (u, v) =>
        {
            // Point up: inside when |u| is below a line narrowing toward the top.
            float half = (1f - v) * .5f;
            return v > -.8f && v < .95f && Mathf.Abs(u) < half ? 1f : 0f;
        });
    }
}
