using System;
using TienTuyen.Presentation.Interface;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TienTuyen.Duel.Presentation
{
    /// <summary>
    /// The 1v1 lobby, laid out like the title screen: the arena orbits on the right, a dark fade on the left carries
    /// the title and two cards, one to host (which then shows this machine's address large enough to read aloud,
    /// with a copy button) and one to join by address. A live status line and an error line sit under them. Every
    /// value comes from <see cref="IDuelLobbyHost"/>, read each frame.
    /// </summary>
    public sealed class DuelLobbyView : UiScreen
    {
        private const float Width = 552f;
        private readonly Func<IDuelLobbyHost> host;

        private readonly RectTransform hostIdle, hostActive;
        private readonly TMP_Text address, copyLabel;
        private readonly UiButton hostButton, cancelHostButton, copyButton, joinButton, cancelJoinButton;
        private readonly CanvasGroup hostCard, joinCard;
        private readonly TMP_InputField field;
        private readonly Image fieldOutline;
        private readonly TMP_Text status, error;
        private readonly Image statusDot, errorMark;
        private float copiedAge = 99f, age;

        public UiButton HostButton => hostButton;
        public UiButton BackButton { get; }
        /// <summary>True while the address field has the keyboard, so shortcuts leave typing alone.</summary>
        public bool Typing => field != null && field.isFocused;

        public DuelLobbyView(Transform parent, Func<IDuelLobbyHost> host, Action back) : base("DuelLobby", parent)
        {
            this.host = host;
            var scrim = UiKit.Image("LeftFade", Root, UiSprites.FadeRight, UiTheme.Alpha(UiTheme.Ink, .95f));
            scrim.rectTransform.anchorMax = new Vector2(.8f, 1);
            var bottom = UiKit.Image("BottomFade", Root, UiSprites.FadeUp, UiTheme.Alpha(UiTheme.Ink, .8f));
            bottom.rectTransform.anchorMax = new Vector2(1, 0);
            bottom.rectTransform.sizeDelta = new Vector2(0, 220);
            bottom.rectTransform.pivot = new Vector2(.5f, 0);
            UiKit.Image("Vignette", Root, UiSprites.Vignette, UiTheme.Alpha(UiTheme.Ink, .55f));

            var column = UiKit.Node("Column", Root, UiKit.MiddleLeft, UiKit.MiddleLeft, new Vector2(86, 10), new Vector2(560, 600));
            Motion = column;
            BasePosition = column.anchoredPosition;
            SlideFrom = new Vector2(-36, 0);

            UiKit.Box("EyebrowRule", column, UiKit.TopLeft, UiKit.TopLeft, new Vector2(0, -10), new Vector2(34, 2), UiSprites.White, UiTheme.Gold);
            Top(UiKit.Caption("Eyebrow", column, "ĐỐI KHÁNG 1V1  ·  MẠNG LAN", 13, UiTheme.Gold), 46, -2, 480, 18);
            var title = Top(UiKit.Text("Title", column, "ĐẤU TAY ĐÔI", UiTheme.Display, 84, UiTheme.Paper, TextAlignmentOptions.TopLeft, 1f), -4, -16, 580, 104);
            title.textWrappingMode = TextWrappingModes.NoWrap;
            UiTheme.Shadow(title);
            Top(UiKit.Text("Tagline", column, "Tối đa 5 hiệp, mỗi hiệp một mạng — thắng 3 hiệp là thắng trận.\nHai máy cùng mạng LAN, hoặc nối thẳng bằng địa chỉ IP.",
                UiTheme.Body, 17, UiTheme.Alpha(UiTheme.Paper, .86f), TextAlignmentOptions.TopLeft), 0, -120, Width, 50);

            // Host card: a button while idle; the address, copy and cancel while hosting.
            var hostRect = Card(column, "HostCard", -188, 156, "MỞ PHÒNG", "1");
            hostCard = hostRect.gameObject.AddComponent<CanvasGroup>();
            hostIdle = UiKit.Rect("Idle", hostRect);
            Place(UiKit.Text("Blurb", hostIdle, "Bạn làm chủ phòng. Máy bạn sẽ hiện địa chỉ để đọc cho đối thủ.",
                UiTheme.Body, 15, UiTheme.Muted, TextAlignmentOptions.TopLeft), 20, -40, Width - 40, 40);
            hostButton = UiButton.Create("Host", hostIdle, UiButton.Style.Primary, "MỞ PHÒNG CHỜ ĐỐI THỦ", () => host()?.RequestHost());
            UiKit.Place(hostButton.GetComponent<RectTransform>(), UiKit.BottomLeft, UiKit.BottomLeft, new Vector2(20, 18), new Vector2(Width - 40, 52));

            hostActive = UiKit.Rect("Hosting", hostRect);
            Place(UiKit.Caption("AddressCaption", hostActive, "ĐỊA CHỈ CỦA BẠN  ·  ĐỌC CHO ĐỐI THỦ", 11, UiTheme.Muted), 20, -40, 360, 16);
            address = Place(UiKit.Text("Address", hostActive, "", UiTheme.Display, 44, UiTheme.GoldBright, TextAlignmentOptions.TopLeft, 3f), 18, -56, 370, 50);
            address.textWrappingMode = TextWrappingModes.NoWrap;
            address.enableAutoSizing = true;
            address.fontSizeMin = 26;
            address.fontSizeMax = 44;
            UiTheme.Shadow(address);
            copyButton = UiButton.Create("Copy", hostActive, UiButton.Style.Secondary, "SAO CHÉP", Copy);
            UiKit.Place(copyButton.GetComponent<RectTransform>(), UiKit.TopRight, UiKit.TopRight, new Vector2(-20, -62), new Vector2(132, 40));
            copyLabel = copyButton.Label;
            cancelHostButton = UiButton.Create("CancelHost", hostActive, UiButton.Style.Secondary, "HỦY PHÒNG", () => host()?.RequestCancel());
            UiKit.Place(cancelHostButton.GetComponent<RectTransform>(), UiKit.BottomLeft, UiKit.BottomLeft, new Vector2(20, 14), new Vector2(Width - 40, 34));

            // Join card: the address field and its button.
            var joinRect = Card(column, "JoinCard", -356, 118, "VÀO PHÒNG CỦA BẠN BÈ", "2");
            joinCard = joinRect.gameObject.AddComponent<CanvasGroup>();
            field = AddressField(joinRect, out fieldOutline);
            joinButton = UiButton.Create("Join", joinRect, UiButton.Style.Secondary, "VÀO PHÒNG", Join);
            UiKit.Place(joinButton.GetComponent<RectTransform>(), UiKit.BottomRight, UiKit.BottomRight, new Vector2(-20, 18), new Vector2(156, 48));
            cancelJoinButton = UiButton.Create("CancelJoin", joinRect, UiButton.Style.Secondary, "HỦY", () => host()?.RequestCancel());
            UiKit.Place(cancelJoinButton.GetComponent<RectTransform>(), UiKit.BottomRight, UiKit.BottomRight, new Vector2(-20, 18), new Vector2(156, 48));

            // Status and error lines.
            statusDot = UiKit.Box("StatusDot", column, UiKit.TopLeft, UiKit.TopLeft, new Vector2(2, -486), new Vector2(9, 9), UiSprites.Disc, UiTheme.Gold);
            status = Top(UiKit.Text("Status", column, "", UiTheme.Body, 16, UiTheme.Paper), 20, -478, Width - 20, 24);
            status.textWrappingMode = TextWrappingModes.NoWrap;
            status.overflowMode = TextOverflowModes.Ellipsis;
            errorMark = UiKit.Box("ErrorMark", column, UiKit.TopLeft, UiKit.TopLeft, new Vector2(0, -510), new Vector2(14, 14), UiSprites.Disc, UiTheme.Danger);
            var bang = UiKit.Text("Bang", errorMark.transform, "!", UiTheme.Display, 11, UiTheme.Ink, TextAlignmentOptions.Center);
            bang.rectTransform.anchoredPosition = new Vector2(0, .5f);
            error = Top(UiKit.Text("Error", column, "", UiTheme.Body, 15, UiTheme.Danger, TextAlignmentOptions.TopLeft), 20, -507, Width - 20, 46);
            // Two lines at full size; a longer message from the connection layer shrinks rather than runs into "back".
            error.enableAutoSizing = true;
            error.fontSizeMin = 12f;
            error.fontSizeMax = 15f;

            BackButton = UiButton.Create("Back", column, UiButton.Style.Secondary, "VỀ MENU CHÍNH", back);
            UiKit.Place(BackButton.GetComponent<RectTransform>(), UiKit.TopLeft, UiKit.TopLeft, new Vector2(0, -566), new Vector2(200, 38));

            var hints = UiKit.HintRow("Controls", Root, 12f, "ENTER", "Vào phòng", "CTRL+C", "Sao chép địa chỉ", "ESC", "Hủy / quay lại");
            UiKit.Place(hints, UiKit.BottomLeft, UiKit.BottomLeft, new Vector2(86, 30), new Vector2(0, 22));
            hints.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            hints.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            var facts = UiKit.Caption("Arena", Root, "BÃI ĐẤU ĐỐI XỨNG  ·  9 VẬT CẢN  ·  CHẠM 3 HIỆP", 10, UiTheme.Faint, TextAlignmentOptions.BottomRight);
            UiKit.Place(facts, UiKit.BottomRight, UiKit.BottomRight, new Vector2(-30, 30), new Vector2(460, 16));
        }

        /// <summary>A card with a small caption and a key chip, its top at <paramref name="y"/> in the column.</summary>
        private static RectTransform Card(RectTransform column, string name, float y, float height, string caption, string key)
        {
            var card = UiKit.Card(name, column, UiTheme.Alpha(UiTheme.Panel, .86f), UiTheme.Alpha(UiTheme.Paper, .12f), 6f);
            UiKit.Place(card.rectTransform, UiKit.TopLeft, UiKit.TopLeft, new Vector2(0, y), new Vector2(Width, height));
            var chip = UiKit.Keycap(card.transform, key, 11f);
            UiKit.Place(chip, UiKit.TopLeft, UiKit.TopLeft, new Vector2(18, -12), new Vector2(22, 21));
            Place(UiKit.Caption("Caption", card.transform, caption, 12, UiTheme.Paper), 50, -14, 400, 18);
            return card.rectTransform;
        }

        private TMP_InputField AddressField(RectTransform card, out Image outline)
        {
            var box = UiKit.Image("Address", card, UiSprites.Rounded(5f), UiTheme.Alpha(UiTheme.Ink, .62f));
            box.raycastTarget = true;
            UiKit.Place(box.rectTransform, UiKit.BottomLeft, UiKit.BottomLeft, new Vector2(20, 18), new Vector2(Width - 40 - 156 - 12, 48));
            outline = UiKit.Image("Outline", box.transform, UiSprites.Outline(5f, 1f), UiTheme.Alpha(UiTheme.Paper, .18f));
            var area = UiKit.Inset(UiKit.Rect("TextArea", box.transform), 14, 6, 14, 6);
            area.gameObject.AddComponent<RectMask2D>();
            var placeholder = UiKit.Text("Placeholder", area, "Địa chỉ IP, ví dụ 192.168.1.23", UiTheme.Body, 17, UiTheme.Faint);
            placeholder.textWrappingMode = TextWrappingModes.NoWrap;
            var text = UiKit.Text("Text", area, "", UiTheme.Display, 22, UiTheme.Paper, TextAlignmentOptions.MidlineLeft, 2f);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.richText = false;
            var input = box.gameObject.AddComponent<TMP_InputField>();
            input.textViewport = area;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.fontAsset = UiTheme.Display;
            input.characterLimit = 64;
            input.contentType = TMP_InputField.ContentType.Standard;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.caretColor = UiTheme.GoldBright;
            input.customCaretColor = true;
            input.caretWidth = 2;
            input.selectionColor = UiTheme.Alpha(UiTheme.Gold, .35f);
            input.targetGraphic = box;
            input.transition = Selectable.Transition.None;
            input.onSubmit.AddListener(_ => Join());
            return input;
        }

        private void Join() => host()?.RequestJoin(field.text);

        /// <summary>Puts the hosted address on the clipboard.</summary>
        public void Copy()
        {
            var current = host();
            if (current == null || string.IsNullOrEmpty(current.JoinInfo)) return;
            GUIUtility.systemCopyBuffer = current.JoinInfo;
            copiedAge = 0f;
        }

        /// <summary>Focuses the address field, for the "2" shortcut.</summary>
        public void FocusAddress()
        {
            if (field.interactable) field.ActivateInputField();
        }

        public string Address { get => field.text; set => field.text = value; }

        protected override void OnShow() => age = 0f;

        public void Refresh(float deltaTime)
        {
            age += deltaTime;
            copiedAge += deltaTime;
            var current = host();
            bool busy = current != null && current.Busy;
            bool hosting = busy && !string.IsNullOrEmpty(current.JoinInfo);
            bool joining = busy && !hosting;

            hostIdle.gameObject.SetActive(!hosting);
            hostActive.gameObject.SetActive(hosting);
            hostButton.Button.interactable = current != null && !busy;
            hostCard.alpha = joining ? .3f : 1f;
            if (hosting && address.text != current.JoinInfo) address.text = current.JoinInfo;
            copyLabel.text = copiedAge < 1.6f ? "ĐÃ CHÉP" : "SAO CHÉP";

            joinCard.alpha = hosting ? .3f : 1f;
            field.interactable = current != null && !busy;
            joinButton.gameObject.SetActive(!joining);
            cancelJoinButton.gameObject.SetActive(joining);
            joinButton.Button.interactable = current != null && !busy;
            fieldOutline.color = Color.Lerp(UiTheme.Alpha(UiTheme.Paper, .18f), UiTheme.Gold, field.isFocused ? 1f : 0f);
            BackButton.Button.interactable = !busy;

            string line = current?.Status ?? "";
            if (current == null) line = "Chưa có kết nối mạng.";
            status.text = line;
            float pulse = .5f + .5f * Mathf.Sin(age * 5f);
            statusDot.enabled = line.Length > 0;
            statusDot.color = busy ? UiTheme.Alpha(UiTheme.Gold, .35f + .65f * pulse) : UiTheme.Health;
            string problem = current?.LastError ?? "";
            error.text = problem;
            errorMark.gameObject.SetActive(problem.Length > 0);
        }

        private static T Top<T>(T graphic, float x, float y, float width, float height) where T : Graphic =>
            UiKit.Place(graphic, UiKit.TopLeft, UiKit.TopLeft, new Vector2(x, y), new Vector2(width, height));

        private static T Place<T>(T graphic, float x, float y, float width, float height) where T : Graphic =>
            UiKit.Place(graphic, UiKit.TopLeft, UiKit.TopLeft, new Vector2(x, y), new Vector2(width, height));
    }
}
