using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TienTuyen.Presentation.Interface
{
    public sealed class SettingsView : ModalScreen
    {
        public Slider Sensitivity { get; }
        public UiButton CloseButton { get; }
        private readonly TMP_Text valueLabel;

        public SettingsView(Transform parent, Action close) :
            base("PlayerSettings", parent, new Vector2(620, 380))
        {
            Eyebrow.text = "ĐIỀU KHIỂN";
            Title.text = "CÀI ĐẶT";
            var label = UiKit.Text("MouseLabel", Panel, "ĐỘ NHẠY CHUỘT", UiTheme.Display, 21, UiTheme.Paper);
            UiKit.Place(label, UiKit.TopLeft, UiKit.TopLeft, new Vector2(34, -122), new Vector2(400, 30));
            valueLabel = UiKit.Text("MouseValue", Panel, "", UiTheme.Display, 24, UiTheme.Gold, TextAlignmentOptions.MidlineRight);
            UiKit.Place(valueLabel, UiKit.TopRight, UiKit.TopRight, new Vector2(-34, -122), new Vector2(100, 30));

            var track = UiKit.Box("MouseSensitivity", Panel, UiKit.TopLeft, UiKit.TopLeft,
                new Vector2(34, -172), new Vector2(552, 32), UiSprites.Rounded(4), UiTheme.Alpha(UiTheme.Paper, .12f));
            track.raycastTarget = true;
            var area = UiKit.Rect("HandleArea", track.transform);
            UiKit.Inset(area, 12, 0, 12, 0);
            var handle = UiKit.Box("Handle", area, UiKit.MiddleLeft, UiKit.Center, Vector2.zero,
                new Vector2(24, 32), UiSprites.Rounded(4), UiTheme.Gold);
            handle.raycastTarget = true;
            Sensitivity = track.gameObject.AddComponent<Slider>();
            Sensitivity.handleRect = handle.rectTransform;
            Sensitivity.targetGraphic = handle;
            Sensitivity.direction = Slider.Direction.LeftToRight;
            Sensitivity.minValue = PlayerSettings.MinSensitivity;
            Sensitivity.maxValue = PlayerSettings.MaxSensitivity;
            Sensitivity.onValueChanged.AddListener(value =>
            {
                PlayerSettings.MouseSensitivity = value;
                RefreshValue();
            });

            var hint = UiKit.Text("Hint", Panel, "25%: chậm hơn  ·  100%: mặc định  ·  300%: nhanh hơn\nÁp dụng cho cả hai góc nhìn và đối kháng 1V1.",
                UiTheme.Body, 15, UiTheme.Muted);
            UiKit.Place(hint, UiKit.TopLeft, UiKit.TopLeft, new Vector2(34, -218), new Vector2(552, 48));
            var reset = UiButton.Create("Reset", Panel, UiButton.Style.Secondary, "MẶC ĐỊNH", () =>
            {
                PlayerSettings.MouseSensitivity = 1f;
                Reload();
            });
            UiKit.Place(reset.GetComponent<RectTransform>(), UiKit.BottomLeft, UiKit.BottomLeft, new Vector2(34, 30), new Vector2(270, 50));
            CloseButton = UiButton.Create("Close", Panel, UiButton.Style.Primary, "LƯU VÀ QUAY LẠI", close);
            UiKit.Place(CloseButton.GetComponent<RectTransform>(), UiKit.BottomRight, UiKit.BottomRight, new Vector2(-34, 30), new Vector2(270, 50));
            Reload();
        }

        protected override void OnShow() => Reload();
        private void Reload()
        {
            Sensitivity.SetValueWithoutNotify(PlayerSettings.MouseSensitivity);
            RefreshValue();
        }
        private void RefreshValue() => valueLabel.text = $"{PlayerSettings.MouseSensitivity * 100f:0}%";
    }
}
