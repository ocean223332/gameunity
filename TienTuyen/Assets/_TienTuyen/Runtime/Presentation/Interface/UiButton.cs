using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TienTuyen.Presentation.Interface
{
    /// <summary>
    /// Animated button skin: hover/focus highlight, focus outline, press scale and
    /// a persistent "chosen" state for selectable cards. Runs on unscaled time so it
    /// keeps responding while combat is paused.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public sealed class UiButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        ISelectHandler, IDeselectHandler, IPointerDownHandler, IPointerUpHandler
    {
        public enum Style { Primary, Secondary, Card }

        public Button Button { get; private set; }
        public TMP_Text Label { get; private set; }
        public RectTransform Content { get; private set; }
        public bool Chosen { get; set; }
        /// <summary>Optional colour override for the card fill (for example an unaffordable offer).</summary>
        public Color? Tint { get; set; }

        private Style style;
        private Image fill, outline, accent, glow;
        private CanvasGroup group;
        private bool hovered, focused, pressed;
        private float highlight, chosen;

        public static UiButton Create(string name, Transform parent, Style style, string caption, Action onClick, float radius = 6f)
        {
            var fill = UiKit.Image(name, parent, UiSprites.Rounded(radius), Color.white);
            fill.raycastTarget = true;
            var root = fill.gameObject;
            var skin = root.AddComponent<UiButton>();
            skin.style = style;
            skin.fill = fill;
            skin.group = root.AddComponent<CanvasGroup>();
            skin.Button = root.GetComponent<Button>();
            skin.Button.transition = Selectable.Transition.None;
            skin.Button.targetGraphic = fill;
            if (onClick != null) skin.Button.onClick.AddListener(() => onClick());

            if (style == Style.Primary)
            {
                skin.glow = UiKit.Image("Glow", fill.transform, UiSprites.Glow, UiTheme.Alpha(UiTheme.Gold, 0));
                UiKit.Inset(skin.glow.rectTransform, -40, -34, -40, -34);
                skin.glow.transform.SetAsFirstSibling();
            }
            skin.outline = UiKit.Image("Outline", fill.transform, UiSprites.Outline(radius, style == Style.Card ? 1.5f : 1f), Color.clear);
            if (style == Style.Card)
            {
                skin.accent = UiKit.Image("ChosenBar", fill.transform, UiSprites.Rounded(1.5f), UiTheme.Gold);
                UiKit.Place(skin.accent.rectTransform, UiKit.TopCenter, new Vector2(.5f, 1), new Vector2(0, -1), new Vector2(0, 3));
                skin.accent.rectTransform.anchorMin = new Vector2(0, 1);
                skin.accent.rectTransform.anchorMax = new Vector2(1, 1);
                skin.accent.rectTransform.offsetMin = new Vector2(radius + 4, -4);
                skin.accent.rectTransform.offsetMax = new Vector2(-radius - 4, -1);
            }
            skin.Content = UiKit.Rect("Content", fill.transform);
            if (caption != null)
            {
                bool primary = style == Style.Primary;
                skin.Label = UiKit.Text("Label", skin.Content, caption, UiTheme.Display, primary ? 21f : 17f,
                    primary ? UiTheme.Ink : UiTheme.Paper, TextAlignmentOptions.Center, primary ? 8f : 5f);
                skin.Label.textWrappingMode = TextWrappingModes.NoWrap;
            }
            skin.Refresh(1f);
            return skin;
        }

        private void Awake() => Button = GetComponent<Button>();

        public void OnPointerEnter(PointerEventData eventData) => hovered = true;
        public void OnPointerExit(PointerEventData eventData) { hovered = false; pressed = false; }
        public void OnSelect(BaseEventData eventData) => focused = true;
        public void OnDeselect(BaseEventData eventData) => focused = false;
        public void OnPointerDown(PointerEventData eventData) => pressed = true;
        public void OnPointerUp(PointerEventData eventData) => pressed = false;

        private void OnDisable()
        {
            hovered = pressed = false;
            highlight = 0;
        }

        private void Update() => Refresh(Time.unscaledDeltaTime);

        private void Refresh(float deltaTime)
        {
            bool interactable = Button != null && Button.IsInteractable();
            float speed = deltaTime * 12f;
            highlight = Mathf.MoveTowards(highlight, interactable && (hovered || focused) ? 1f : 0f, speed);
            chosen = Mathf.MoveTowards(chosen, Chosen ? 1f : 0f, speed);
            float focus = interactable && focused ? 1f : 0f;
            group.alpha = interactable ? 1f : .42f;

            switch (style)
            {
                case Style.Primary:
                    fill.color = Color.Lerp(UiTheme.Gold, UiTheme.GoldBright, highlight);
                    outline.color = UiTheme.Alpha(Color.white, .55f * focus);
                    if (glow != null) glow.color = UiTheme.Alpha(UiTheme.Gold, .1f + .22f * highlight);
                    break;
                case Style.Secondary:
                    fill.color = Color.Lerp(UiTheme.Alpha(UiTheme.PanelRaised, .88f), UiTheme.Alpha(UiTheme.PanelHover, .96f), highlight);
                    outline.color = Color.Lerp(UiTheme.Alpha(UiTheme.Paper, .14f), UiTheme.Alpha(UiTheme.Gold, .9f), Mathf.Max(focus, chosen));
                    if (Label != null) Label.color = Color.Lerp(UiTheme.Paper, UiTheme.GoldBright, chosen);
                    break;
                default:
                    Color baseFill = Tint ?? UiTheme.Alpha(UiTheme.Panel, .86f);
                    fill.color = Color.Lerp(baseFill, UiTheme.Alpha(UiTheme.PanelHover, .95f), highlight * .8f);
                    Color idle = UiTheme.Alpha(UiTheme.Paper, .12f + .3f * highlight);
                    outline.color = Color.Lerp(idle, UiTheme.Gold, Mathf.Max(chosen, focus * .75f));
                    accent.color = UiTheme.Alpha(UiTheme.Gold, chosen);
                    break;
            }
            float scale = pressed && interactable ? .975f : 1f + highlight * (style == Style.Card ? .018f : .01f);
            transform.localScale = Vector3.one * scale;
        }
    }
}
