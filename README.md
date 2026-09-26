# Tiền Tuyến

Game sinh tồn bắn súng top-down lấy cảm hứng từ vòng chơi Brotato, bối cảnh trạm tiếp tế hư cấu tại Trường Sơn năm 1972. Nhân vật thuộc Quân đội Nhân dân Việt Nam; đây không phải bản tái hiện một trận đánh có thật.

## Cấu hình đã chốt

- Unity **6000.3.24f1**, template Universal 3D, URP.
- Windows, một người chơi, offline; không backend hoặc Unity Cloud.
- Repository public: [ocean223332/gameunity](https://github.com/ocean223332/gameunity), gồm tài liệu và Unity project.
- Combat slice P2 chạy trong scene riêng `CombatSpike.unity`: 6 đợt sinh tồn, 3 vũ khí, 3 địch thường (gồm lính xung kích mở từ wave 2) và elite enemy, kiện tiếp tế wave 3, nhặt tài nguyên, pause/restart, nâng cấp cuối wave và shop 4 ô. `ProgressionRun`/`ShopSession` sở hữu currency, XP, passive, upgrade, reroll, khóa, mua/bán/ghép và giữ tối đa 4 slot vũ khí.

## Mở project

1. Trong Unity Hub, chọn **Add project from disk** và chọn thư mục `TienTuyen` nằm cạnh README này. Không chọn thư mục cha `gameunity`.
2. Mở bằng Unity **6000.3.24f1** và chờ import/package resolution hoàn tất. Game chạy offline; lần thiết lập/import đầu có thể cần mạng để lấy package Unity.
3. Mở scene `Assets/_TienTuyen/Scenes/CombatSpike.unity`. Nhấn Play, chọn Rifle hoặc SMG rồi bắt đầu. Dùng WASD/phím mũi tên để di chuyển; nhân vật tự ngắm và tự bắn; `Esc` để tạm dừng.

Git giữ `Assets` cùng mọi file `.meta`, `Packages/manifest.json`, `Packages/packages-lock.json` và `ProjectSettings`. Cache, log, file IDE và build đầu ra không được lưu vào Git.

## Kiểm chứng nền móng

- Import và compile: đạt, Unity 6000.3.24f1.
- Scene `CombatSpike.unity`: lưu thành công, đã kiểm tra hình camera trong Play Mode với arena, cover, người chơi, địch và pickup.
- Test tự động sau chỉnh hoạt ảnh và vị trí súng: **57/57 Edit Mode** (gồm 4 Foundation) và **24/24 Play Mode** đạt.
- Nhân vật low-poly có hoạt ảnh khớp theo mã: đứng/chạy, ngắm, cầm súng hai tay, giật súng và nạp đạn; đã kiểm tra hình học chống súng xuyên thân trên Rifle/SMG/Shotgun.
- Windows x64 Mono: build P2 thành công tại `Builds/Windows/TienTuyen.exe`, 105.848.797 byte, 14,9 giây, 0 lỗi và 1 warning; warning chỉ cho biết Pipeline runtime bị tắt trong Player build.
- Integrity check: **271 file**, 0 lỗi, 0 cảnh báo, 0 mục không kiểm tra được sau khi hoàn thiện asset/settings.
- Git: nhánh `main`; cache, build, `TestResults/` và `video_output/` không được đưa lên remote.

Chi tiết, giới hạn và các warning công cụ: [SETUP_STATUS](docs/SETUP_STATUS.md).

## Test và build lại

- Trong Unity: **Window → General → Test Runner → EditMode/PlayMode**, chạy các assembly `TienTuyen.Foundation.Tests`, `TienTuyen.Combat.Editor.Tests` và `TienTuyen.Combat.Play.Tests`.
- Menu **Tien Tuyen → Combat → Create Scene** tạo scene combat một lần và chủ động từ chối ghi đè scene đã có. **Tien Tuyen → Combat → Build Windows (Queued)** hoặc **Tien Tuyen → Foundation → Build Windows** tạo `Builds/Windows/TienTuyen.exe` ở thư mục repository. Đóng bản `.exe` đang chạy trước khi build lại.
- Không chạy lại **Create Baseline** trên scene đã tạo: công cụ chủ động từ chối ghi đè. Từ đây chỉnh scene bằng Editor, không sửa YAML bằng tay.
- UI runtime dùng uGUI + TextMeshPro, gồm menu chọn vũ khí, HUD máu/wave/thời gian/XP/tiền/đạn, pause và màn hình kết quả. Cinemachine chưa cần cho camera cố định.

Build và compile không thay thế playtest dài đủ 6 wave. Hiện đã có test lifecycle tự động và kiểm tra artifact build; cân bằng, full human playthrough, profiler và các hệ thống P3 chưa được tuyên bố hoàn tất. Nội dung P2 hiện có đủ ba archetype địch thường cùng elite; art/audio đại diện và full human playthrough vẫn còn mở.

## Tài liệu

- [SPEC — gameplay và tiêu chí nghiệm thu](docs/SPEC.md)
- [PLAN — milestone và kiến trúc](docs/PLAN.md)
- [ART_DIRECTION — phong cách và asset](docs/ART_DIRECTION.md)

Các tài liệu thiết kế mô tả đích đến; tính năng chỉ được coi là có khi đã triển khai và kiểm chứng. Quyết định cấu hình đã chốt phía trên thay thế các ghi chú “chưa chốt” tương ứng trong tài liệu đề xuất cũ.
