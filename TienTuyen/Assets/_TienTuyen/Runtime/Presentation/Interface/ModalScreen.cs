using TMPro;
using UnityEngine;

namespace TienTuyen.Presentation.Interface
{
    /// <summary>Centred panel over a darkened, vignetted backdrop with an eyebrow and title header.</summary>
    public abstract class ModalScreen : UiScreen
    {
        protected readonly RectTransform Panel;
        protected readonly TMP_Text Eyebrow, Title;
        private readonly UnityEngine.UI.Image rule;

        protected ModalScreen(string name, Transform parent, Vector2 size, bool framed = true) : base(name, parent)
        {
            var backdrop = UiKit.Image("Backdrop", Root, UiSprites.White, UiTheme.Alpha(UiTheme.Ink, .5f));
            backdrop.raycastTarget = true;
            UiKit.Image("Vignette", Root, UiSprites.Vignette, UiTheme.Alpha(UiTheme.Ink, .7f));

            // Panel is a plain container; the shadow and card are its first children so content draws on top.
            Panel = UiKit.Node("Panel", Root, UiKit.Center, new Vector2(.5f, .5f), Vector2.zero, size);
            if (framed)
            {
                var shadow = UiKit.Image("Shadow", Panel, UiSprites.Glow, UiTheme.Alpha(Color.black, .6f));
                UiKit.Inset(shadow.rectTransform, -110, -130, -110, -90);
                UiKit.Card("Card", Panel, UiTheme.Alpha(UiTheme.Panel, .96f), UiTheme.Alpha(UiTheme.Paper, .1f), 10f);
            }
            Motion = Panel;
            BasePosition = Vector2.zero;
            SlideFrom = new Vector2(0, -22);

            rule = UiKit.Box("Rule", Panel, UiKit.TopLeft, UiKit.TopLeft, new Vector2(34, -32), new Vector2(30, 2), UiSprites.White, UiTheme.Gold);
            Eyebrow = UiKit.Caption("Eyebrow", Panel, "", 13, UiTheme.Gold);
            UiKit.Place(Eyebrow, UiKit.TopLeft, UiKit.TopLeft, new Vector2(74, -24), new Vector2(size.x - 108, 18));
            Title = UiKit.Text("Title", Panel, "", UiTheme.Display, 42, UiTheme.Paper, TextAlignmentOptions.TopLeft, 2f);
            UiKit.Place(Title, UiKit.TopLeft, UiKit.TopLeft, new Vector2(32, -44), new Vector2(size.x - 64, 60));
            Title.textWrappingMode = TextWrappingModes.NoWrap;
        }

        protected void Accent(Color color)
        {
            rule.color = color;
            Eyebrow.color = color;
        }
    }
}
