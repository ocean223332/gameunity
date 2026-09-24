# Tiền Tuyến — trạng thái nền Unity

## Đã triển khai

- Người dùng duyệt Unity hiện có, URP, Windows offline và Git local, không đưa lên mạng.
- Unity project: `../TienTuyen`, Editor **6000.3.24f1**, template `com.unity.template.urp-blank` (Universal 3D).
- URP **17.3.0**, Input System **1.20.0**, Test Framework **1.6.0**, uGUI **2.0.0** có sẵn trong template; Pipeline **0.7.0-exp.1** thêm bằng CLI để điều khiển Editor cục bộ.
- Giữ nguyên các package phụ của template, chưa cấu hình AI Navigation, Multiplayer Center, Visual Scripting hoặc Timeline thành tính năng. Không thêm IAP, quảng cáo, backend hoặc remote repository.
- Scene `Assets/_TienTuyen/Scenes/Bootstrap.unity`: camera orthographic size12, nghiêng60°, nền thử34×20, một capsule và hai hộp mẫu màu. Các khối không có gameplay/collider.
- PC quality, Linear color, HDR, directional light/bóng mềm; Global Volume ACES, Bloom0.5/threshold0.9, Vignette0.15. Material dùng shader URP/Lit.
- Windows64 Mono, 1280×720 windowed; ForceText, Visible Meta Files. Scene Bootstrap là scene duy nhất bật trong build settings.
- UI stack dự kiến: uGUI + TextMeshPro; chưa tạo HUD/menu runtime.

## Bằng chứng kiểm tra

| Kiểm tra | Kết quả |
|---|---|
| Script compile | Hoàn tất, không compile errors |
| Baseline validator | Đạt trước/sau Play và build |
| Edit Mode tests | 4/4 đạt, lần cuối 0,34s; camera, profile đã lưu, materials, build config |
| Edit/Play render | Đã xem ảnh, ground và swatch có màu/bóng; không material hồng |
| Integrity CLI | 95 file, 0 lỗi, 0 cảnh báo, 0 mục không kiểm tra được |
| Windows build | Thành công; build cuối incremental 3,2s; 102.934.004 byte theo báo cáo trả về |
| Standalone smoke | Khởi động batchmode có graphics, RTX3060 được nhận, load assemblies/scene; tiến trình phản hồi, không exception/shader error trong log kiểm tra. Đã dừng đúng tiến trình thử sau kiểm tra |
| Network configuration | Cloud link trống, Unity Connect/ads/analytics tắt; Pipeline `enableInBuilds=false`, `autoStart=false`; không thấy TCP socket của tiến trình tại thời điểm kiểm tra |

Ảnh: [Edit Mode](art/unity-baseline-edit.png), [Play Mode](art/unity-baseline-play.png). Đây là ảnh thực từ Unity, không phải concept đã tạo trước đó.

Máy tham chiếu ban đầu: Intel i7-12700H, RAM16GB, RTX3060 Laptop GPU6GB; chưa benchmark FPS/gameplay. Việc khởi động nền không chứng minh thao tác bàn phím, gameplay hoặc vận hành hoàn toàn không mạng ở mọi tình huống.

## Ghi chú công cụ

- CLI beta8 không nhận `--caller`/`--skill` trên lệnh eval dù bản skill mới mô tả chúng; dùng schema thực tế đã truy vấn.
- Build đầu qua eval đồng bộ vượt timeout5s của Pipeline nhưng vẫn hoàn tất; lỗi timeout công cụ xuất hiện trong báo cáo đầu. Đã gọi build lại qua Editor update callback và nhận log thành công3,2s.
- Cảnh báo build của Pipeline xác nhận runtime bridge bị tắt, là trạng thái mong muốn. Package vẫn có assembly runtime nội bộ; không tuyên bố toàn bộ package được loại khỏi Player.
- Editor có warning Hub IPC timeout; không chặn compile/render/test/build trong lượt này. Player ghi cảnh báo D3D12 info-queue nhưng tiếp tục khởi tạo GPU và load scene.
- Ảnh chụp Edit Mode đầu quá sớm chỉ có clear color; đã chụp lại sau khi render cập nhật và xác minh.
- Chưa kiểm thử clean checkout hoặc giao diện standalone trực tiếp. Build outputs/log/cache được .gitignore loại khỏi lịch sử.

## Tiếp theo: combat spike P1

Nền project đã sẵn sàng. Chưa có di chuyển, vũ khí, địch, damage, wave, pause/restart hoặc asset mỹ thuật cuối. Bước kế tiếp: tạo scene combat riêng; 1 nhân vật, 2 vũ khí, 2 archetype địch, 3 đợt; kiểm chứng input/LOS/collision trước mở rộng. Không cần mua/sinh model Thrixel cho bước này.
