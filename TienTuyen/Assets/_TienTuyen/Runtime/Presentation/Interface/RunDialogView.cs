using System;
using TMPro;
using TienTuyen.Combat;
using UnityEngine;
using UnityEngine.UI;

namespace TienTuyen.Presentation.Interface
{
    /// <summary>Pause menu and end-of-run report (victory or defeat).</summary>
    public sealed class RunDialogView : ModalScreen
    {
        private static readonly Vector2 Size = new Vector2(620, 404);
        private readonly CombatGame game;
        private readonly RectTransform pauseBody, resultBody;
        private readonly TMP_Text[] statValues = new TMP_Text[6];
        private readonly TMP_Text runLine;

        public UiButton Primary { get; }
        public UiButton Secondary { get; }

        public RunDialogView(Transform parent, CombatGame game, Action primary, Action secondary) : base("RunDialog", parent, Size)
        {
            this.game = game;

            pauseBody = UiKit.Node("Controls", Panel, UiKit.TopLeft, UiKit.TopLeft, new Vector2(34, -122), new Vector2(Size.x - 68, 200));
            string[][] rows =
            {
                new[] { "W+A+S+D", "Di chuyển" },
                new[] { "CHUỘT", "Xoay và ngắm" },
                new[] { "CHUỘT TRÁI", "Bắn" },
                new[] { "CHUỘT PHẢI", "Ngắm kỹ" },
                new[] { "R", "Nạp đạn" },
                new[] { "ESC", "Tiếp tục" },
            };
            for (int i = 0; i < rows.Length; i++)
            {
                var row = UiKit.HintRow("Row" + i, pauseBody, 14f, rows[i][0], rows[i][1]);
                row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
                UiKit.Place(row, UiKit.TopLeft, UiKit.TopLeft, new Vector2(i % 2 * 280, -(i / 2) * 36), new Vector2(270, 26));
            }
            var tip = UiKit.Text("Tip", pauseBody, "Bao cát chặn cả di chuyển lẫn đường đạn — hãy núp sau chúng khi nạp đạn.",
                UiTheme.Body, 15, UiTheme.Muted, TextAlignmentOptions.TopLeft);
            UiKit.Place(tip, UiKit.TopLeft, UiKit.TopLeft, new Vector2(0, -124), new Vector2(Size.x - 68, 44));

            resultBody = UiKit.Node("Results", Panel, UiKit.TopLeft, UiKit.TopLeft, new Vector2(34, -118), new Vector2(Size.x - 68, 200));
            string[] captions = { "ĐỢT", "THỜI GIAN", "HẠ ĐỊCH", "SÁT THƯƠNG", "TIẾP TẾ", "CẤP ĐỘ" };
            const float cellWidth = 176f, cellHeight = 68f;
            for (int i = 0; i < captions.Length; i++)
            {
                var cell = UiKit.Card("Stat" + i, resultBody, UiTheme.Alpha(UiTheme.Ink, .45f), UiTheme.Alpha(UiTheme.Paper, .07f), 6f);
                UiKit.Place(cell.rectTransform, UiKit.TopLeft, UiKit.TopLeft, new Vector2(i % 3 * (cellWidth + 8), -(i / 3) * (cellHeight + 8)), new Vector2(cellWidth, cellHeight));
                var caption = UiKit.Caption("Caption", cell.transform, captions[i], 10, UiTheme.Muted, TextAlignmentOptions.TopLeft);
                UiKit.Place(caption, UiKit.TopLeft, UiKit.TopLeft, new Vector2(14, -10), new Vector2(150, 14));
                statValues[i] = UiKit.Text("Value", cell.transform, "", UiTheme.Display, 28, UiTheme.Paper, TextAlignmentOptions.BottomLeft);
                UiKit.Place(statValues[i], UiKit.BottomLeft, UiKit.BottomLeft, new Vector2(13, 6), new Vector2(156, 40));
            }
            runLine = UiKit.Text("RunLine", resultBody, "", UiTheme.Body, 14, UiTheme.Muted, TextAlignmentOptions.TopLeft);
            UiKit.Place(runLine, UiKit.TopLeft, UiKit.TopLeft, new Vector2(0, -160), new Vector2(Size.x - 68, 22));

            float half = (Size.x - 68 - 12) * .5f;
            Primary = UiButton.Create("Primary", Panel, UiButton.Style.Primary, "", primary);
            UiKit.Place(Primary.GetComponent<RectTransform>(), UiKit.BottomLeft, UiKit.BottomLeft, new Vector2(34, 32), new Vector2(half, 54));
            Secondary = UiButton.Create("Secondary", Panel, UiButton.Style.Secondary, "VỀ MENU CHÍNH", secondary);
            UiKit.Place(Secondary.GetComponent<RectTransform>(), UiKit.BottomLeft, UiKit.BottomLeft, new Vector2(34 + half + 12, 32), new Vector2(half, 54));
        }

        /// <summary>Fills the dialog for the current state (called when it opens).</summary>
        public void Present(string weaponName)
        {
            bool paused = game.State == CombatState.Paused;
            pauseBody.gameObject.SetActive(paused);
            resultBody.gameObject.SetActive(!paused);
            Primary.Label.text = paused ? "TIẾP TỤC" : "CHƠI LẠI";
            if (paused)
            {
                Accent(UiTheme.Gold);
                Eyebrow.text = $"ĐỢT {game.Wave} / {game.MaxWave}  ·  THỜI GIAN ĐÃ DỪNG";
                Title.text = "TẠM DỪNG";
                return;
            }
            bool victory = game.State == CombatState.Victory;
            Accent(victory ? UiTheme.Gold : UiTheme.Danger);
            Eyebrow.text = victory ? "CHIẾN THẮNG" : "TRẠM TIẾP TẾ THẤT THỦ";
            Title.text = victory ? "GIỮ VỮNG TRẬN ĐỊA" : "LƯỢT CHƠI KẾT THÚC";
            int seconds = Mathf.FloorToInt(game.RunElapsed);
            statValues[0].text = $"{game.Wave}<size=16><color=#A2AB98> / {game.MaxWave}</color></size>";
            statValues[1].text = $"{seconds / 60}:{seconds % 60:00}";
            statValues[2].text = game.Kills.ToString();
            statValues[3].text = game.DamageDealt.ToString("0");
            statValues[4].text = game.Currency.ToString();
            statValues[5].text = game.Level.ToString();
            runLine.text = $"Bộ binh  ·  {weaponName}  ·  Seed {game.Seed}";
        }
    }
}
