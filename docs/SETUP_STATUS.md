# Tiền Tuyến — trạng thái Unity và combat slice P2

## Đã triển khai

- Người dùng duyệt Unity hiện có, URP, Windows offline; sau đó yêu cầu công khai mã nguồn lên GitHub tại `ocean223332/gameunity`.
- Unity project: `../TienTuyen`, Editor **6000.3.24f1**, template `com.unity.template.urp-blank` (Universal 3D).
- URP **17.3.0**, Input System **1.20.0**, Test Framework **1.6.0**, uGUI **2.0.0** có sẵn trong template; Pipeline **0.7.0-exp.1** thêm bằng CLI để điều khiển Editor cục bộ.
- Giữ nguyên các package phụ của template, chưa cấu hình AI Navigation, Multiplayer Center, Visual Scripting hoặc Timeline thành tính năng. Không thêm IAP, quảng cáo hoặc backend.
- Scene nền `Assets/_TienTuyen/Scenes/Bootstrap.unity` vẫn giữ nguyên cho kiểm tra project.
- Scene chơi `Assets/_TienTuyen/Scenes/CombatSpike.unity`: arena top-down orthographic có cover, player, enemy pool, projectile/pickup pool, spawn warning và ánh sáng URP. Build Settings chỉ bật scene combat.
- PC quality, Linear color, HDR, directional light/bóng mềm; Global Volume ACES, Bloom0.5/threshold0.9, Vignette0.15. Material dùng shader URP/Lit.
- Windows64 Mono, 1280×720 windowed; ForceText, Visible Meta Files. Scene CombatSpike là scene duy nhất bật trong build settings.
- UI runtime: uGUI + TextMeshPro, menu Rifle/SMG, HUD máu/wave/time/XP/currency/ammo/reload, pause, defeat/victory và restart/menu.
- Combat slice hiện tại: 6 wave (wave 5–6 dài 50 giây), Rifle/SMG/Shotgun tự bắn và tự nạp, infantry/shooter/charger/elite với telegraph, cover/LOS, di chuyển lưới, pickup currency/XP, pause/death/restart, wave settlement và kiện tiếp tế wave 3 (20 tiếp tế + hồi 10 HP). Charger mở từ wave 2, báo trước lane rồi dash theo hướng đã khóa.
- Module P2 thuần C# đã nối vào combat: `ProgressionRun`/`ShopSession` cho 4 slot, passive stack, upgrade pending, giá/reroll, khóa, mua/bán/ghép và băng bó; `PassiveCatalog` có 6 ID ổn định I01–I06; `ContentCatalog` có 3 vũ khí, 3 enemy thường và 1 elite; `SupplyEvent` xử lý progress/claim idempotent. Presentation có màn hình nâng cấp và shop tiếng Việt, preview trước/sau và các thao tác giao dịch.

## Bằng chứng kiểm tra

Cập nhật hoạt ảnh 2026-09-27: **57/57 Edit Mode**, **24/24 Play Mode** đạt. Đã bổ sung hoạt ảnh khớp low-poly, cầm súng hai tay, recoil/reload, muzzle flash/vỏ đạn và kiểm tra hình học của ba súng với áo/giáp. Đã xem cận cảnh từ hai góc ở tư thế đứng, chạy, bắn và nạp đạn. Các thông số build trong bảng bên dưới là bản P2 trước lần cập nhật hoạt ảnh, không phải xác nhận build mới. Log và ảnh kiểm thử mới lưu cục bộ trong `TestResults/`.

| Kiểm tra | Kết quả |
|---|---|
| Script compile | Hoàn tất sau khi import TMP và combat assemblies; không compile errors mới |
| Foundation validator | Đạt; build scene trỏ tới CombatSpike |
| Combat Edit Mode tests | **53/53 đạt**: combat rules, P2 catalog, charger eligibility/stats, progression transaction/cap, passive catalog, idempotent reward và supply lifecycle |
| Combat Play Mode tests | **11/11 đạt**: full health/start, Shotgun P2, pause clock, invulnerability, defeat reward, charger telegraph/dash, charger reward idempotency, full 6-wave upgrade/shop→Victory flow, upgrade→shop flow, shop transactions và 20 restart/pool reset |
| Foundation Edit Mode tests | **4/4 đạt** |
| Edit/Play render | Đã xem camera thật trong Play Mode: arena, cover, player, enemy và pickup hiển thị đúng; menu tiếng Việt đã kiểm tra |
| Integrity CLI | **271 file**, 0 lỗi, 0 cảnh báo, 0 mục không kiểm tra được |
| Windows build | Build P2 thành công; `Builds/Windows/TienTuyen.exe`, 105.848.797 byte, 14,9 giây, 0 lỗi, 1 warning (Pipeline runtime tắt trong Player) |
| Standalone smoke | Đã mở executable Windows, cửa sổ `Tiền Tuyến` hiển thị, phím bắt đầu chuyển qua menu/play flow và đóng được bằng Alt+F4 |
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

## Giới hạn P2 và bước tiếp theo

Slice hiện vẫn dùng primitive graybox và material URP, chưa phải art final. Chưa có nhiều lớp nhân vật, audio hoàn chỉnh hoặc profiling stress. Bộ địch P2 đã đủ ba archetype thường cùng elite; full human playthrough 6 wave vẫn còn. TMP cảnh báo một số ký tự tiếng Việt ngoài glyph nền và dùng fallback/dynamic glyph khi cần; cần chốt font hỗ trợ đầy đủ trước P2 hoàn chỉnh.

Bước kế tiếp là hoàn thiện kiểm chứng P2: một góc art/audio hoàn thiện đại diện, font tiếng Việt đầy đủ, rồi chạy playthrough đủ 6 wave trên Windows build.
