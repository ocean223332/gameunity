# TIỀN TUYẾN — Art direction & asset plan v0.1

Ngày: 25/09/2026. **Đề xuất để duyệt**, chưa phải art đã triển khai hoặc benchmark. Gameplay theo [SPEC.md](SPEC.md), lịch sản xuất theo [PLAN.md](PLAN.md). Không đổi 12 đợt, 4 ô vũ khí hay phạm vi MVP.

## 1. Hướng hình ảnh

**3D stylized low-poly với màu vẽ tay**, nghiêm túc vừa phải, hình khối gọn và chất liệu ít nhiễu. Không chibi, không photoreal, không pixel art. Nhân vật có tỉ lệ gần người thật nhưng bàn tay, súng và túi trang bị hơi lớn để nhìn rõ. Không dùng phóng đại cơ thể kiểu quái vật cho đối phương.

- Camera orthographic cố định, nghiêng khoảng 60° so với mặt đất; arena trong một khung 16:9. Duyệt asset ở góc camera này trước góc cận cảnh.
- Rừng ô-liu trầm, đất nâu ấm, ánh sáng ban ngày dịu. Cỏ thưa trong đường chạy; tán cây tập trung ngoài rìa, làm mờ khi che nhân vật/cảnh báo.
- Bóng mềm, ít phản xạ; hạn chế bloom, sương dày và depth of field. Không thêm mưa động trong MVP.
- Thứ tự thị giác: người chơi → nguy hiểm → mục tiêu tiếp tế → địch → vật cản → trang trí.

Địa điểm là trạm tiếp tế hư cấu ở Trường Sơn năm 1972. Quân phục, phù hiệu, súng và đối phương hiện là placeholder: phải duyệt reference lịch sử trước art cuối. Vai trò gameplay không chứng minh một loại trang bị có thật trong đơn vị.

## 2. Bảng màu thử nghiệm

| Vai trò | Màu khởi điểm | Cách nhận biết bổ sung |
|---|---|---|
| Rừng / bóng nền | `#354638`, `#24352D` | Mảng lớn, chi tiết thưa |
| Đất / đường | `#80664C` | Bề mặt ít tương phản |
| Người chơi | `#8E9B66` | Viền sáng, vòng chân có dấu chevron |
| Địch | `#766C5E` | Silhouette theo vai trò, không chỉ đổi màu |
| Tiếp tế | `#E7BD62` | Biểu tượng thùng + vòng tiến độ |
| Nguy hiểm | `#F17858` | Viền sáng/tối, hình vùng và đếm ngược |
| UI chữ / nền | `#EEE7D4`, `#202A25` | Chữ rõ dấu Việt, nền bảng đặc |

Màu là giả thuyết art, chưa kiểm tra tương phản. Đạn địch dùng chấm sáng có đuôi ngắn; tracer người chơi dùng vệt mảnh. Chế độ xem xám vẫn phải phân biệt được nguy hiểm, pickup và người chơi.

## 3. Danh mục asset theo mốc

Số lượng dưới đây là tổng tích lũy; dùng chung mesh/rig khi hợp lý, không bắt buộc mỗi vai trò là một model mới hoàn toàn.

| Nhóm | P1: combat spike | P2: vertical slice | P3: MVP |
|---|---|---|---|
| Nhân vật | 1 placeholder | 1 mẫu art hoàn chỉnh | 3 lớp: bộ binh, trinh sát, yểm trợ |
| Vũ khí | 2 placeholder | 3 model + icon | 6 vai trò + 6 icon |
| Địch | 2 placeholder | 3 thường + 1 elite | 6 thường + 1 elite + 1 boss |
| Vật phẩm | Chưa cần | 6 icon | 18 icon riêng, không cần 18 model 3D |
| Arena | 1 graybox | 1 góc hoàn thiện đại diện | 1 arena rừng thống nhất |
| Tiếp tế | Pickup thường | Thêm kiện mục tiêu | Dùng lại tại đợt 3/6/9 |
| UI/VFX | HUD và phản hồi tối thiểu | Menu, shop, upgrade, kết quả | Đủ trạng thái, accessibility |

Sáu vũ khí: súng trường, tiểu liên, trung liên, tản đạn, súng trường chính xác, bộ phóng lựu đạn; mẫu súng thật để sau kiểm chứng. Tier I/II/III dùng nhãn UI, chưa tạo 18 mesh. Bốn slot luôn hiện trên HUD; đề xuất một súng chính trên tay, các slot còn lại có điểm phát hiệu ứng nhỏ. Kiểm chứng khả năng đọc ở P1 trước chốt, không thêm đồng đội AI.

Sáu địch: áp sát, xung kích, xạ thủ, ném lựu đạn, lính nặng, yểm trợ. Khác biệt qua dáng đứng, kích thước túi/súng và động tác báo đòn; không dùng trang phục thiếu căn cứ chỉ để phân loại. Elite nhấn bằng trang bị và marker, không kéo giãn thành người khổng lồ. Boss là tổ hỏa lực hư cấu có người lính tỉ lệ bình thường; cấu trúc hành vi vẫn theo SPEC, không thêm xe điều khiển hoặc cơ chế mới.

### Bộ môi trường modular

Đề xuất 12–16 module tái sử dụng: đoạn/góc bao cát, hòm đóng/mở, mái che, cột, thùng, khúc gỗ, hai đá, hai cây, bụi, cỏ và mảng đất. Ghép thủ công thành arena; không sinh nguyên map thành một mesh. Bao cát/đá có collider rõ; cỏ/lá không collider. Mỗi cụm cover có ít nhất hai lối vòng. Tách mái/tán để xử lý che khuất.

## 4. Rig, animation và budget tạm

Một skeleton humanoid dùng chung nơi phù hợp; biến thể qua trang bị và mesh. Bộ cơ bản: idle, chạy, recoil/bắn, reload, hit, chết; thêm ngắm, lao và ném cho các vai trò cần thiết. Không ragdoll. Animation không được trì hoãn hoặc rút ngắn cảnh báo gameplay đã quy định.

| Asset | Mức thử ban đầu, không phải giới hạn đã đo |
|---|---|
| Nhân vật | 2.000–4.000 triangles; 1–2 material |
| Vũ khí | 300–1.000 triangles/model |
| Prop nhỏ / cây | 100–800 / 500–1.500 triangles |
| Texture | Atlas nhân vật 1K; môi trường 1K–2K; VFX 256–512 |
| Icon | Nguồn 256×256, kiểm tra ở kích thước hiển thị 48–64 px |

Ưu tiên atlas, material dùng chung, pool VFX và ít lớp lá trong suốt. Chỉ khóa budget sau đo standalone trên reference PC theo PLAN; không suy ra 60 FPS từ số polygon.

## 5. UI, VFX và nguồn asset

UI gợi bảng quân nhu bằng màu giấy/ô-liu, không phủ texture cũ lên chữ. Shop có bốn thẻ hàng; inventory có bốn ô vũ khí. Cấp trang bị có số La Mã; trạng thái khóa có icon; cảnh báo nổ có vòng, nét và thời gian, không phụ thuộc đỏ/xanh. Hit flash ngắn, không che telegraph; cho phép tắt shake/damage numbers. Âm thanh nguy hiểm hỗ trợ hình ảnh, không thay thế hình ảnh.

Nguồn có thể dùng: tự dựng, đặt artist, thư viện có license phù hợp, hoặc sinh model sau khi duyệt chi phí. Không mặc định asset miễn phí là dùng thương mại được. Lưu nguồn, tác giả, license, bằng chứng quyền sử dụng và chỉnh sửa trong manifest; kiểm tra riêng nhạc/font/animation. Chưa mua, tải pack, cài công cụ hoặc sinh model trong bước này.

Ảnh concept/keyframe dùng để duyệt màu, bố cục và phong cách; **không phải mesh, rig, texture atlas hay screenshot gameplay Unity**. Bản visualization giúp thảo luận mật độ, HUD và vùng nhìn; không chứng minh hiệu năng hoặc đường đi.

## 6. Gate duyệt và QA

1. Duyệt một keyframe combat và bảng màu ở camera thật.
2. Duyệt một hero cùng súng/rig mẫu; kiểm tra hình ở 720p trước sản xuất cả catalog.
3. Ghép vào blockout; kiểm chứng đạn, cover, tán cây và bốn slot; sau đó hoàn thiện góc vertical slice.
4. Mở rộng đủ MVP khi pipeline art đã ổn.

QA: thử 720p/1080p, xem xám, combat đông và tắt shake; không mất dấu người chơi/đạn/telegraph. Mesh đúng scale/pivot, không missing material, animation không trượt chân rõ rệt, tiếng Việt không mất dấu. So hình với collider; kiểm tra license và reference lịch sử trước gắn nhãn art final.

Cần duyệt tiếp: hướng 3D này; mức cường điệu nhân vật; cách thể hiện bốn vũ khí; diện mạo/lực lượng đối phương sau nghiên cứu. Chưa cần chốt mọi asset trước combat spike.

## 7. Minh họa v01

- [Concept màu và không gian](art/concept-v01.png), tạo bằng công cụ image generation tích hợp; [prompt và ghi chú review](art/CONCEPT_PROMPT.md).
- [Ảnh kiểm tra bố cục HUD](art/hud-layout-qa-v01.png): sơ đồ ký hiệu, không phải art game. Bản tương tác đi kèm trong hội thoại chuyển giữa chiến đấu và quân nhu.
- Concept đang chi tiết hơn budget đề xuất; production cần giảm nhiễu mặt đất/lá cây và làm rõ khác biệt đạn hai phía. Chi tiết quân phục/súng trong ảnh chưa được duyệt lịch sử.
- Kiểm tra bản HUD trong trình duyệt: chuyển hai màn đúng, không runtime error trong lượt kiểm tra, không tràn ngang tại 320 px. Chưa kiểm chứng Unity, cảm ứng thật hoặc hiệu năng gameplay.
