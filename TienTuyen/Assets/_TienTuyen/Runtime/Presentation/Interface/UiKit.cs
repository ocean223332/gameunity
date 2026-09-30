using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TienTuyen.Presentation.Interface
{
    /// <summary>Small builders for runtime-authored uGUI. Positions are in 1280x720 canvas units.</summary>
    public static class UiKit
    {
        public static readonly Vector2 TopLeft = new Vector2(0, 1), TopCenter = new Vector2(.5f, 1), TopRight = Vector2.one;
        public static readonly Vector2 MiddleLeft = new Vector2(0, .5f), Center = new Vector2(.5f, .5f), MiddleRight = new Vector2(1, .5f);
        public static readonly Vector2 BottomLeft = Vector2.zero, BottomCenter = new Vector2(.5f, 0), BottomRight = new Vector2(1, 0);

        /// <summary>A rect stretched over its parent.</summary>
        public static RectTransform Rect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        /// <summary>A rect of fixed size pinned to one anchor point of its parent.</summary>
        public static RectTransform Node(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size) =>
            Place(Rect(name, parent), anchor, pivot, position, size);

        public static RectTransform Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }

        public static RectTransform Inset(RectTransform rect, float left, float bottom, float right, float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
            return rect;
        }

        public static Image Image(string name, Transform parent, Sprite sprite, Color color)
        {
            var image = Rect(name, parent).gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            if (sprite != null && sprite.border != Vector4.zero) image.type = UnityEngine.UI.Image.Type.Sliced;
            return image;
        }

        public static Image Box(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size, Sprite sprite, Color color)
        {
            var image = Image(name, parent, sprite, color);
            Place(image.rectTransform, anchor, pivot, position, size);
            return image;
        }

        /// <summary>Glyph image that keeps its aspect ratio.</summary>
        public static Image Icon(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size, Sprite sprite, Color color)
        {
            var image = Box(name, parent, anchor, pivot, position, size, sprite, color);
            image.type = UnityEngine.UI.Image.Type.Simple;
            image.preserveAspect = true;
            return image;
        }

        /// <summary>Rounded card: translucent fill plus a hairline outline.</summary>
        public static Image Card(string name, Transform parent, Color fill, Color outline, float radius = 6f)
        {
            var card = Image(name, parent, UiSprites.Rounded(radius), fill);
            Image("Outline", card.transform, UiSprites.Outline(radius, 1f), outline);
            return card;
        }

        public static TMP_Text Text(string name, Transform parent, string text, TMP_FontAsset font, float size, Color color,
            TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft, float spacing = 0f)
        {
            var label = Rect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.characterSpacing = spacing;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Overflow;
            label.raycastTarget = false;
            label.richText = true;
            return label;
        }

        /// <summary>Upper-case, letter-spaced condensed caption.</summary>
        public static TMP_Text Caption(string name, Transform parent, string text, float size, Color color,
            TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft) =>
            Text(name, parent, text, UiTheme.Display, size, color, alignment, 9f);

        public static T Place<T>(T graphic, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size) where T : Graphic
        {
            Place(graphic.rectTransform, anchor, pivot, position, size);
            return graphic;
        }

        /// <summary>Keyboard key chip; sizes itself to its label.</summary>
        public static RectTransform Keycap(Transform parent, string key, float size = 12f)
        {
            var cap = Image("Key_" + key, parent, UiSprites.Rounded(3.5f), UiTheme.Alpha(UiTheme.Paper, .1f));
            Image("Edge", cap.transform, UiSprites.Outline(3.5f, 1f), UiTheme.Alpha(UiTheme.Paper, .42f));
            var layout = cap.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(7, 7, 2, 1);
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            if (parent.GetComponent<LayoutGroup>() == null)
                cap.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            var label = Text("Label", cap.transform, key, UiTheme.Display, size, UiTheme.Paper, TextAlignmentOptions.Center, 4f);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            var element = cap.gameObject.AddComponent<LayoutElement>();
            element.minWidth = size + 12f;
            element.minHeight = element.preferredHeight = size + 10f;
            // The outline ignores layout so it always covers the whole chip.
            cap.transform.GetChild(0).gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            return cap.rectTransform;
        }

        /// <summary>Row of "[key] action" hints, laid out automatically.</summary>
        public static RectTransform HintRow(string name, Transform parent, float size, params string[] keyThenAction)
        {
            var row = Rect(name, parent);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            for (int i = 0; i + 1 < keyThenAction.Length; i += 2)
            {
                foreach (string key in keyThenAction[i].Split('+')) Keycap(row, key, size - 2f);
                var action = Text("Action", row, keyThenAction[i + 1], UiTheme.Body, size + 1f, UiTheme.Alpha(UiTheme.Paper, .82f));
                action.textWrappingMode = TextWrappingModes.NoWrap;
                UiTheme.Shadow(action);
                if (i + 2 < keyThenAction.Length)
                    Rect("Gap", row).gameObject.AddComponent<LayoutElement>().preferredWidth = 12f;
            }
            return row;
        }
    }

    /// <summary>Rounded progress bar whose fill shrinks in width (so both ends stay rounded) with an optional lagging trail.</summary>
    public sealed class UiBar
    {
        public readonly RectTransform Root;
        public readonly Image Track, Fill, Trail;
        private float value = 1f, trail = 1f, trailHold;

        public UiBar(string name, Transform parent, Color fill, bool withTrail = false, float radius = 2.5f)
        {
            Track = UiKit.Image(name, parent, UiSprites.Rounded(radius), UiTheme.Alpha(UiTheme.Ink, .6f));
            Root = Track.rectTransform;
            if (withTrail) Trail = Segment("Trail", UiTheme.Alpha(UiTheme.Paper, .7f), radius);
            Fill = Segment("Fill", fill, radius);
        }

        private Image Segment(string name, Color color, float radius)
        {
            var image = UiKit.Image(name, Root, UiSprites.Rounded(radius), color);
            image.rectTransform.anchorMax = new Vector2(1, 1);
            return image;
        }

        /// <summary>Sets the fill fraction; the trail catches up after a short hold.</summary>
        public void Set(float fraction, float deltaTime)
        {
            fraction = Mathf.Clamp01(fraction);
            if (Trail != null)
            {
                if (fraction < value) { trail = Mathf.Max(trail, value); trailHold = .45f; }
                trailHold -= deltaTime;
                trail = trailHold > 0 ? trail : Mathf.MoveTowards(trail, fraction, deltaTime * .8f);
                trail = Mathf.Max(trail, fraction);
                Width(Trail, trail);
            }
            value = fraction;
            Width(Fill, fraction);
        }

        private static void Width(Image image, float fraction)
        {
            image.rectTransform.anchorMax = new Vector2(fraction, 1);
            image.enabled = fraction > .003f;
        }
    }
}
