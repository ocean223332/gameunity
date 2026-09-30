using System.Collections.Generic;
using TienTuyen.Content;
using TienTuyen.Progression;
using UnityEngine;

namespace TienTuyen.Presentation.Interface
{
    /// <summary>
    /// White vector silhouettes (weapons, items, upgrades) rasterised once with
    /// supersampling. Shapes are applied in order, so later shapes can add or cut.
    /// Tint them with <see cref="UnityEngine.UI.Image.color"/>.
    /// </summary>
    public static class UiGlyphs
    {
        private static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

        public static Sprite Weapon(string weaponId)
        {
            if (weaponId == ContentIds.Smg) return Get("smg", 512, 160, Smg);
            if (weaponId == ContentIds.Shotgun) return Get("shotgun", 512, 160, Shotgun);
            return Get("rifle", 512, 160, Rifle);
        }

        public static Sprite Passive(string passiveId)
        {
            switch (passiveId)
            {
                case PassiveCatalog.MedKit: return Get("medkit", 128, 128, MedKit);
                case PassiveCatalog.Bandage: return Get("bandage", 128, 128, BandageRoll);
                case PassiveCatalog.Armor: return Get("shield", 128, 128, Shield);
                case PassiveCatalog.Boots: return Get("boot", 128, 128, Boot);
                case PassiveCatalog.Sling: return Get("sling", 128, 128, Sling);
                case PassiveCatalog.CleaningKit: return Get("wrench", 128, 128, Wrench);
                default: return Supply;
            }
        }

        public static Sprite Upgrade(UpgradeKind kind)
        {
            switch (kind)
            {
                case UpgradeKind.Health: return Get("heart", 128, 128, Heart);
                case UpgradeKind.Damage: return Get("bullet", 128, 128, Bullet);
                case UpgradeKind.AttackSpeed: return Get("chevrons", 128, 128, Chevrons);
                case UpgradeKind.Armor: return Get("shield", 128, 128, Shield);
                case UpgradeKind.Speed: return Get("bolt", 128, 128, Bolt);
                default: return Get("regen", 128, 128, Regen);
            }
        }

        /// <summary>Ammunition crate used for the supply currency.</summary>
        public static Sprite Supply => Get("crate", 128, 128, Crate);

        public static Sprite Diamond => Get("diamond", 64, 64, s => s.Poly(16, 2, 30, 16, 16, 30, 2, 16));

        // ---------- Silhouettes (design space: weapons 100 x 32, icons 32 x 32, y up) ----------

        private static void Rifle(Shape s)
        {
            // SKS-style carbine: long wooden stock, fixed magazine, folded bayonet.
            s.Poly(1, 9.4f, 2.2f, 17.2f, 8, 18f, 28, 18.8f, 32, 18.8f, 32, 14.2f, 27, 13.4f, 14, 11.2f, 6, 9f);
            s.Rect(31, 14.2f, 52, 19.6f);
            s.Rect(33, 19.4f, 50, 20.8f);
            s.Poly(52, 14.8f, 52, 19.6f, 71, 19.2f, 73.5f, 17.8f, 73.5f, 15.6f);
            s.Rect(52, 19.4f, 75, 20.8f);
            s.Rect(72, 16.8f, 97, 18.4f);
            s.Rect(91.6f, 18.2f, 93, 21.2f);
            s.Rect(95.4f, 16.3f, 98.4f, 18.9f);
            s.Poly(38.5f, 14.4f, 47.5f, 14.4f, 46.4f, 10.2f, 39.6f, 10.2f);
            s.Rect(31.6f, 11.2f, 38, 12.3f);
            s.Rect(31.6f, 11.2f, 32.6f, 14.4f);
            s.Poly(71, 15.1f, 89, 15.5f, 92.2f, 15.9f, 89, 16.3f, 71, 16.1f);
            // Seams between wood and steel.
            s.Cut(30.6f, 14.6f, 31.3f, 18.4f);
            s.Cut(51.4f, 15, 52.1f, 19.2f);
        }

        private static void Smg(Shape s)
        {
            // K-50M style: wire stock, pistol grip, vented shroud, curved box magazine.
            s.Rect(3, 9.4f, 5.6f, 18.8f);
            s.Rect(3, 17.4f, 28, 18.8f);
            s.Poly(3, 9.4f, 3, 10.8f, 28, 15.4f, 28, 14);
            s.Poly(30, 14, 35.6f, 14, 33.6f, 6.4f, 28.4f, 6.9f);
            s.Poly(26, 13.6f, 26, 19.4f, 56, 19.4f, 58.4f, 17.8f, 58.4f, 15, 56, 13.6f);
            s.Rect(47, 19.2f, 52.5f, 20.6f);
            s.Rect(56, 15, 81, 19.2f);
            for (int i = 0; i < 5; i++) s.CutCircle(61 + i * 4.3f, 17.1f, .95f);
            s.Rect(80, 15.7f, 87, 18.5f);
            s.Rect(83, 18.3f, 84.4f, 20.4f);
            s.Poly(40, 13.8f, 46.6f, 13.8f, 48.4f, 8.6f, 50.6f, 3.6f, 44.4f, 2.6f, 42.4f, 7.6f);
            s.Rect(34.8f, 11.6f, 40.4f, 12.5f);
            s.Rect(39.6f, 11.6f, 40.4f, 13.8f);
        }

        private static void Shotgun(Shape s)
        {
            // Pump-action: long barrel over tube magazine, ribbed fore-end.
            s.Poly(1, 9, 1.8f, 17.4f, 8, 18.2f, 24, 18.4f, 30, 17.8f, 30, 13.6f, 23, 12.4f, 7, 8.4f);
            s.Poly(29.5f, 13.6f, 29.5f, 19.2f, 45, 19.2f, 47, 18.2f, 47, 13.6f);
            s.Rect(46, 17.3f, 97.5f, 19.1f);
            s.Rect(46, 14.4f, 85, 16.9f);
            s.Rect(84, 14.2f, 86.4f, 17.2f);
            s.Rect(54, 13.4f, 72, 17.5f);
            for (int i = 0; i < 6; i++) s.Cut(56.2f + i * 2.8f, 13.9f, 56.9f + i * 2.8f, 17);
            s.Rect(95.8f, 19, 96.9f, 19.9f);
            s.Rect(32, 11.4f, 38.4f, 12.3f);
            s.Rect(37.5f, 11.4f, 38.4f, 13.8f);
            s.Cut(29, 14, 29.6f, 17.6f);
        }

        private static void MedKit(Shape s)
        {
            s.RoundRect(3, 6, 29, 26, 3);
            s.Rect(12, 2.5f, 20, 7);
            s.Cut(14, 4, 18, 7);
            s.Cut(13.5f, 9, 18.5f, 23);
            s.Cut(9, 13.5f, 23, 18.5f);
        }

        private static void BandageRoll(Shape s)
        {
            // Adhesive plaster: strip with a centre pad and breathing holes.
            s.RoundRect(1, 10, 31, 22, 6);
            s.Cut(10.4f, 10, 11.6f, 22);
            s.Cut(20.4f, 10, 21.6f, 22);
            foreach (float x in new[] { 5.2f, 26.8f })
            {
                s.CutCircle(x, 13.8f, .9f);
                s.CutCircle(x, 18.2f, .9f);
            }
        }

        private static void Shield(Shape s)
        {
            s.Poly(4, 27, 16, 30, 28, 27, 28, 15, 16, 2, 4, 15);
            s.Cut(15, 5, 17, 28);
        }

        private static void Boot(Shape s)
        {
            s.Poly(7, 29, 17, 29, 17, 13, 27, 10.5f, 29, 7.5f, 29, 5, 5, 5, 5, 12);
            s.Cut(5, 7, 29, 8.2f);
            s.Cut(9, 20, 17, 21.2f);
            s.Cut(9, 16, 17, 17.2f);
        }

        private static void Sling(Shape s)
        {
            s.Circle(16, 16, 13);
            s.CutCircle(16, 16, 9.5f);
            s.Cut(0, 14, 32, 32);
            s.Rect(4, 13, 7.5f, 24);
            s.Rect(24.5f, 13, 28, 24);
            s.RoundRect(10, 21, 22, 27, 1.5f);
            s.Cut(12.5f, 23, 19.5f, 25);
        }

        private static void Wrench(Shape s)
        {
            s.Poly(4, 25, 7, 28, 23.5f, 11.5f, 20.5f, 8.5f);
            s.Circle(24, 8, 6.5f);
            s.CutCircle(24, 8, 2.8f);
            s.Cut(24, 5.4f, 32, 10.6f);
        }

        private static void Heart(Shape s)
        {
            s.Circle(10.5f, 20, 7);
            s.Circle(21.5f, 20, 7);
            s.Poly(4, 17.5f, 28, 17.5f, 16, 4);
        }

        private static void Bullet(Shape s)
        {
            s.Rect(11, 4, 21, 18);
            s.Poly(11, 18, 21, 18, 19.5f, 24, 16, 29, 12.5f, 24);
            s.Cut(11, 17.2f, 21, 18.2f);
            s.Rect(10, 3, 22, 5.5f);
        }

        private static void Chevrons(Shape s)
        {
            s.Poly(4, 4, 10, 4, 18, 16, 10, 28, 4, 28, 12, 16);
            s.Poly(15, 4, 21, 4, 29, 16, 21, 28, 15, 28, 23, 16);
        }

        private static void Bolt(Shape s) => s.Poly(19, 30, 7, 15, 15, 15, 12, 2, 25, 18, 17, 18);

        private static void Regen(Shape s)
        {
            s.Circle(16, 16, 13);
            s.CutCircle(16, 16, 10.2f);
            s.Rect(13.6f, 8, 18.4f, 24);
            s.Rect(8, 13.6f, 24, 18.4f);
        }

        private static void Crate(Shape s)
        {
            // Ammunition box: solid body, lid seam, latch and carry handle.
            s.RoundRect(2, 4, 30, 21, 2);
            s.Cut(2, 15.2f, 30, 16.4f);
            s.Cut(14, 11, 18, 14);
            s.RoundRect(10, 20, 22, 26, 2);
            s.Cut(12.5f, 20, 19.5f, 23.6f);
        }

        // ---------- Rasteriser ----------

        private delegate void Draw(Shape shape);

        private static Sprite Get(string key, int width, int height, Draw draw)
        {
            if (cache.TryGetValue(key, out var sprite) && sprite != null) return sprite;
            var shape = new Shape();
            draw(shape);
            sprite = shape.Rasterise(width, height);
            sprite.name = "Glyph_" + key;
            cache[key] = sprite;
            return sprite;
        }

        private sealed class Shape
        {
            private enum Kind { Poly, Circle, RoundRect }
            private struct Op { public Kind kind; public bool cut; public Vector2[] points; public Vector2 a, b; public float r; }
            private readonly List<Op> ops = new List<Op>();

            public void Poly(params float[] xy)
            {
                var points = new Vector2[xy.Length / 2];
                for (int i = 0; i < points.Length; i++) points[i] = new Vector2(xy[i * 2], xy[i * 2 + 1]);
                ops.Add(new Op { kind = Kind.Poly, points = points });
            }

            public void Rect(float x0, float y0, float x1, float y1) => RoundRect(x0, y0, x1, y1, 0f);
            public void Cut(float x0, float y0, float x1, float y1) =>
                ops.Add(new Op { kind = Kind.RoundRect, cut = true, a = new Vector2(x0, y0), b = new Vector2(x1, y1) });
            public void RoundRect(float x0, float y0, float x1, float y1, float r) =>
                ops.Add(new Op { kind = Kind.RoundRect, a = new Vector2(x0, y0), b = new Vector2(x1, y1), r = r });
            public void Circle(float x, float y, float r) => ops.Add(new Op { kind = Kind.Circle, a = new Vector2(x, y), r = r });
            public void CutCircle(float x, float y, float r) => ops.Add(new Op { kind = Kind.Circle, cut = true, a = new Vector2(x, y), r = r });

            public Sprite Rasterise(int width, int height)
            {
                // Fit the additive shapes' bounds into the texture with a small margin.
                Vector2 min = Vector2.positiveInfinity, max = Vector2.negativeInfinity;
                foreach (var op in ops)
                {
                    if (op.cut) continue;
                    if (op.kind == Kind.Poly) foreach (var p in op.points) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
                    else if (op.kind == Kind.Circle) { min = Vector2.Min(min, op.a - Vector2.one * op.r); max = Vector2.Max(max, op.a + Vector2.one * op.r); }
                    else { min = Vector2.Min(min, op.a); max = Vector2.Max(max, op.b); }
                }
                float margin = 4f;
                float scale = Mathf.Min((width - margin * 2) / (max.x - min.x), (height - margin * 2) / (max.y - min.y));
                Vector2 offset = new Vector2(width, height) * .5f - (min + max) * .5f * scale;

                const int samples = 4;
                var pixels = new Color32[width * height];
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int covered = 0;
                    for (int sy = 0; sy < samples; sy++)
                    for (int sx = 0; sx < samples; sx++)
                    {
                        var p = (new Vector2(x + (sx + .5f) / samples, y + (sy + .5f) / samples) - offset) / scale;
                        if (Inside(p)) covered++;
                    }
                    pixels[y * width + x] = new Color32(255, 255, 255, (byte)(covered * 255 / (samples * samples)));
                }
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, true)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Trilinear,
                    hideFlags = HideFlags.DontSave
                };
                texture.SetPixels32(pixels);
                texture.Apply(true, true);
                return Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(.5f, .5f), 100f);
            }

            private bool Inside(Vector2 p)
            {
                bool inside = false;
                foreach (var op in ops)
                {
                    if (inside == !op.cut) continue;
                    bool hit;
                    switch (op.kind)
                    {
                        case Kind.Circle: hit = (p - op.a).sqrMagnitude <= op.r * op.r; break;
                        case Kind.RoundRect: hit = InRoundRect(p, op.a, op.b, op.r); break;
                        default: hit = InPolygon(p, op.points); break;
                    }
                    if (hit) inside = !op.cut;
                }
                return inside;
            }

            private static bool InRoundRect(Vector2 p, Vector2 a, Vector2 b, float r)
            {
                if (p.x < a.x || p.x > b.x || p.y < a.y || p.y > b.y) return false;
                if (r <= 0) return true;
                float cx = Mathf.Clamp(p.x, a.x + r, b.x - r), cy = Mathf.Clamp(p.y, a.y + r, b.y - r);
                return (p - new Vector2(cx, cy)).sqrMagnitude <= r * r;
            }

            private static bool InPolygon(Vector2 p, Vector2[] points)
            {
                bool inside = false;
                for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
                {
                    if ((points[i].y > p.y) != (points[j].y > p.y) &&
                        p.x < (points[j].x - points[i].x) * (p.y - points[i].y) / (points[j].y - points[i].y) + points[i].x)
                        inside = !inside;
                }
                return inside;
            }
        }
    }
}
