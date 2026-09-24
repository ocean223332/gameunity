# Tiền Tuyến

Game sinh tồn bắn súng top-down lấy cảm hứng từ vòng chơi Brotato, bối cảnh trạm tiếp tế hư cấu tại Trường Sơn năm 1972. Nhân vật thuộc Quân đội Nhân dân Việt Nam; đây không phải bản tái hiện một trận đánh có thật.

## Cấu hình đã chốt

- Unity **6000.3.24f1**, template Universal 3D, URP.
- Windows, một người chơi, offline; không backend hoặc Unity Cloud.
- Git cục bộ tại thư mục này, gồm tài liệu và project; chưa tạo remote hoặc đưa lên mạng.
- Đang dựng nền móng project; **chưa có gameplay**. Combat spike 3 đợt là mốc kế tiếp, không phải tính năng đã hoàn thành.

## Mở project

1. Trong Unity Hub, chọn **Add project from disk** và chọn thư mục `TienTuyen` nằm cạnh README này. Không chọn thư mục cha `gameunity`.
2. Mở bằng Unity **6000.3.24f1** và chờ import/package resolution hoàn tất. Game chạy offline; lần thiết lập/import đầu có thể cần mạng để lấy package Unity.
3. Mở scene `Assets/_TienTuyen/Scenes/Bootstrap.unity`. Nhấn Play để xem nền, ánh sáng và các khối thử màu; chưa có nhân vật điều khiển hoặc chiến đấu.

Git giữ `Assets` cùng mọi file `.meta`, `Packages/manifest.json`, `Packages/packages-lock.json` và `ProjectSettings`. Cache, log, file IDE và build đầu ra không được lưu vào Git.

## Kiểm chứng nền móng

- Import và compile: đạt, Unity 6000.3.24f1.
- Scene `Bootstrap.unity`: lưu thành công, đã kiểm tra hình Edit/Play Mode.
- Test tự động: **4/4 Edit Mode tests đạt** cho camera, profile, materials và build scene.
- Windows x64 Mono: build thành công, khoảng 103 MB theo BuildReport; khởi động nền và đọc log đạt, chưa kiểm thử tương tác bản standalone.
- Integrity check: 95 file kiểm tra, không phát hiện lỗi hoặc cảnh báo.
- Git: repository cục bộ tại `gameunity`, không remote; xem `git log -1` để biết commit nền đầu tiên.

Chi tiết, giới hạn và các warning công cụ: [SETUP_STATUS](docs/SETUP_STATUS.md).

## Test và build lại

- Trong Unity: **Window → General → Test Runner → EditMode**, chạy assembly `TienTuyen.Foundation.Tests`.
- Menu **Tien Tuyen → Foundation → Validate Baseline** kiểm tra cấu hình đã lưu.
- Menu **Tien Tuyen → Foundation → Build Windows** tạo `Builds/Windows/TienTuyen.exe` ở thư mục repository. Đóng bản `.exe` đang chạy trước khi build lại.
- Không chạy lại **Create Baseline** trên scene đã tạo: công cụ chủ động từ chối ghi đè. Từ đây chỉnh scene bằng Editor, không sửa YAML bằng tay.
- UI dự kiến dùng uGUI + TextMeshPro; chưa dựng HUD. Cinemachine chưa cần cho camera cố định.

Không suy ra gameplay đã hoạt động chỉ từ việc project mở hoặc build thành công.

## Tài liệu

- [SPEC — gameplay và tiêu chí nghiệm thu](docs/SPEC.md)
- [PLAN — milestone và kiến trúc](docs/PLAN.md)
- [ART_DIRECTION — phong cách và asset](docs/ART_DIRECTION.md)

Các tài liệu thiết kế mô tả đích đến; tính năng chỉ được coi là có khi đã triển khai và kiểm chứng. Quyết định cấu hình đã chốt phía trên thay thế các ghi chú “chưa chốt” tương ứng trong tài liệu đề xuất cũ.
