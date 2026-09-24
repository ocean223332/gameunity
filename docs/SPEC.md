# TIỀN TUYẾN — Game specification v0.2

Ngày: 25/09/2026. Trạng thái: **bản đề xuất để duyệt**, chưa phải yêu cầu đã chốt.

Quyết định triển khai đã duyệt: Unity 6000.3.24f1 hiện có, Universal 3D/URP, Windows offline, Git cục bộ không remote. Project nằm tại `../TienTuyen`. Hướng art 3D stylized tiếp tục theo ART_DIRECTION; các con số cân bằng vẫn là giả thuyết cần playtest.

Cập nhật v0.2: người dùng chọn “phe Việt Nam” và giao quyền chọn giai đoạn. Cách diễn giải làm việc là nhân vật thuộc Quân đội Nhân dân Việt Nam; chọn năm 1972. Tên lực lượng là diễn giải được nêu rõ để người dùng có thể chỉnh, không coi cụm “phe Việt Nam” tự nó xác định duy nhất một bên trong chiến tranh.

## 1. Tóm tắt sản phẩm

Game bắn súng sinh tồn góc nhìn từ trên xuống, chơi đơn, theo từng đợt tấn công. Người chơi di chuyển né đòn, vũ khí tự khai hỏa, thu tiếp tế và xây dựng bộ trang bị giữa các đợt. Người chơi vào vai một chiến sĩ Quân đội Nhân dân Việt Nam tại một trạm tiếp tế hư cấu ở khu vực Trường Sơn năm 1972; nhân vật, đơn vị cụ thể, địa điểm trạm và trận đánh là hư cấu.

Tên làm việc: **Tiền Tuyến**; chưa kiểm tra tính khả dụng thương mại của tên.

Tham chiếu Brotato ở vòng chơi và cách phối hợp trang bị, không sao chép nhân vật, giao diện, hình ảnh hay bộ chỉ số. Theo [trang giới thiệu chính thức trên Steam](https://store.steampowered.com/app/1942280/Brotato/), Brotato có vũ khí tự bắn, các đợt chiến đấu ngắn và mua đồ giữa đợt. Những con số bên dưới là thiết kế riêng cho prototype này.

### Yêu cầu đã có từ người dùng

- Engine Unity.
- Top-down shooter, cơ chế gần Brotato.
- Bối cảnh chiến tranh Việt Nam.
- Lập plan và spec trước khi lập trình.

### Giả định đề xuất, cần duyệt trước khi tạo project

| Quyết định | Đề xuất v0.1 | Lý do |
|---|---|---|
| Nền tảng nghiệm thu | PC Windows, offline | Tập trung kiểm chứng chiến đấu và build |
| Hình ảnh | 3D stylized, góc nhìn 2.5D, camera orthographic | Thể hiện cây cối, công sự; vẫn đọc được đạn |
| Engine/pipeline | Unity 6 LTS tương thích tại lúc setup, URP | Khóa bản vá và package sau khi kiểm tra môi trường |
| Nhân vật/phe | Quân đội Nhân dân Việt Nam; Trường Sơn, năm 1972 | Diễn giải “phe Việt Nam”; giai đoạn do người dùng giao chọn |
| Độ xác thực | Arcade trong bối cảnh lịch sử, không mô phỏng quân sự | Cho phép nhiều trang bị, nâng cấp và tự bắn |
| Mô hình phát hành | Không ads/IAP/backend trong MVP | Chưa có yêu cầu thương mại |

Nếu chọn 2D thay cho 3D, cần sửa phần art/pipeline/di chuyển trước milestone đầu; không thay đổi âm thầm trong quá trình làm.

### Bối cảnh làm việc: Trạm tiếp tế giữa rừng, 1972

- Pitch nhiệm vụ: giữ vị trí và thu hồi vật tư qua 12 đợt tấn công, chờ đoàn vận tải rời khu vực an toàn. Đoàn xe chỉ là bối cảnh kể chuyện/kết thúc, không phải hệ thống hộ tống AI mới.
- “Giữ trạm” được thể hiện bằng việc sống sót trong arena; trạm không có thanh HP hay điều kiện thua phụ. Luật thắng/thua tại mục 4 không đổi.
- Shop là trạm quân nhu giữa đợt; tiền tiếp tế là điểm phân bổ trang bị mang tính arcade, không mô phỏng mua bán vũ khí thời chiến.
- Hình ảnh định hướng: đường đất, mái che ngụy trang, bao cát, hòm vật tư, cây rừng; quân phục, phù hiệu và mẫu súng phải có reference trước khi làm art cuối.
- Không gọi đây là bản tái hiện một trận đánh có thật; không đồng nhất 600 giây combat với thời lượng sự kiện lịch sử.
- Chọn bối cảnh này vì phù hợp cơ chế địa hình và tiếp tế đã có, không thêm map, phương tiện hoặc hệ thống mới. Đây là quyết định thiết kế theo nguyên tắc giữ phạm vi nhỏ và kiểm chứng vòng chơi của skill game-design.

Nguồn nền: [Báo Quân đội nhân dân — Công tác hậu cần chiến dịch Trị–Thiên 1972](https://hc.qdnd.vn/lich-su-hau-can/cong-tac-hau-can-chien-dich-tri-thien-1972-463504) cung cấp bối cảnh về tổ chức chi viện năm 1972. Nguồn không xác nhận sự tồn tại của trạm hoặc nhiệm vụ hư cấu trong game; chi tiết trang bị và lực lượng đối phương vẫn cần nghiên cứu riêng.

## 2. Trụ cột thiết kế

1. **Di chuyển là kỹ năng chính:** nhìn được nguy hiểm, luôn có đường né hợp lý.
2. **Mỗi lần mua làm thay đổi cách chơi:** tầm xa, nhiều đạn, diện rộng, phòng thủ và kinh tế có đánh đổi.
3. **Bối cảnh ảnh hưởng gameplay:** vật cản chặn đạn và kiện tiếp tế buộc người chơi cân nhắc vị trí.
4. **Một lượt ngắn, có đầu–cuối:** khoảng 13–18 phút với thời gian mua đồ thông thường; người chơi có thể ở shop lâu hơn.

Điểm riêng trong MVP: địa hình có vật cản cố định và mục tiêu tiếp tế tùy chọn. Mưa động, hệ thống tinh thần, phá hủy địa hình và mô phỏng chiến thuật không nằm trong MVP.

## 3. Phạm vi phiên bản

| Nội dung | Combat spike | Vertical slice | MVP |
|---|---:|---:|---:|
| Arena | 1 blockout | 1 hoàn chỉnh một phần | 1 arena rừng |
| Đợt chiến đấu | 3 | 6 | 12 |
| Lớp nhân vật | 1 | 1 | 3 |
| Loại vũ khí | 2 | 3 | 6 |
| Vật phẩm bị động | 0 | 6 | 18 |
| Địch thường | 2 | 3 | 6 |
| Elite / boss | 0 / 0 | 1 / 0 | 1 / 1 |
| Shop và nâng cấp | Không | Đầy đủ vòng cơ bản | Đầy đủ |
| Kiện tiếp tế | Không | 1 sự kiện | Đợt 3, 6, 9 |

MVP có một độ khó; mọi nhân vật và vũ khí đều mở để thử nghiệm. Không có chỉ số tăng vĩnh viễn giữa các lượt.

Ngoài phạm vi: multiplayer/co-op, campaign lịch sử, thế giới mở, phương tiện điều khiển được, đồng đội AI, crafting, cây kỹ năng dài hạn, tài khoản, cloud save, Steam integration, ads/IAP, procedural map, phá hủy công sự, bản phát hành mobile/Web. Có thể lập bản mở rộng sau khi MVP đạt tiêu chí.

## 4. Vòng chơi và trạng thái

`Menu → Chọn nhân vật → Chuẩn bị → Chiến đấu → Tổng kết đợt → Chọn nâng cấp → Trạm tiếp tế → Đợt kế tiếp → Kết quả`

- Bắt đầu với vũ khí cấp I của nhân vật, HP đầy, 0 tiếp tế, cấp nhân vật 1.
- Qua đợt nếu còn sống khi đồng hồ về 0. Không bắt buộc dọn hết địch.
- Đợt 12 thắng khi hết giờ hoặc khi hạ boss. Nếu HP về 0 cùng bước mô phỏng với điều kiện thắng, xử lý tử vong trước.
- Hết đợt: ngừng sinh địch và gây sát thương; thu hồi đạn/địch không trao thêm thưởng; quyết toán tiếp tế còn trên đất; xử lý nâng cấp rồi mở shop.
- Hoàn thành đợt 12 đi thẳng tới kết quả, không có shop hoặc lượt chọn nâng cấp cuối.
- HP hiện có giữ qua đợt; không tự hồi đầy. Băng bó và build hồi phục có giá trị thực.
- Khi bắt đầu đợt mới, mọi vũ khí có băng đầy, cooldown sẵn sàng; không mang tiến độ reload từ đợt trước.
- Chết mất toàn bộ build của lượt. Restart tạo RunState sạch; thống kê và cài đặt được giữ.
- Pause đóng băng gameplay, đồng hồ, cooldown, reload và telegraph; UI vẫn hoạt động.

## 5. Điều khiển và camera

| Hành động | PC | Ánh xạ dự kiến khi thử cảm ứng |
|---|---|---|
| Di chuyển | WASD / phím mũi tên | Joystick ảo trái |
| Bắn / ngắm | Tự động mặc định | Tự động |
| Ngắm thủ công | Giữ chuột phải, hướng về con trỏ; vẫn tự bắn | Chưa nằm trong MVP |
| Tương tác kiện | Đi vào vùng thu hồi | Tương tự |
| Pause | Esc | Nút pause |
| Shop | Chuột; có focus bàn phím | Nút chạm khi thử nghiệm |

Không có dash, nhảy hoặc nút nạp đạn trong MVP. Gamepad và remap đầy đủ là phần mở rộng, không phải tính năng đã hứa.

Camera cố định hướng, nghiêng khoảng 60° so với mặt đất, không xoay tự do. Arena đầu tiên nằm trong một khung hình 16:9; letterbox cho tỉ lệ khác để không thay đổi lợi thế quan sát. Không camera shake mặc định quá mạnh; cho phép giảm hoặc tắt.

## 6. Người chơi và chỉ số

Thông số dưới đây là **giá trị khởi điểm để playtest**, không khẳng định đã cân bằng.

| Chỉ số | Mặc định | Quy tắc |
|---|---:|---|
| HP tối đa | 100 | Tăng HP tối đa cộng cùng lượng vào HP hiện tại, không vượt trần mới |
| Tốc độ | 5 đơn vị/giây | Giới hạn hệ số 0,6–1,8 |
| Giáp | 0 | Damage nhận = raw × 100 / (100 + armor); armor trong 0–100 |
| Damage bonus | 0% | Cộng các modifier cùng nhóm |
| Attack speed bonus | 0% | Interval = base / (1 + bonus); bonus từ -50% đến +100% |
| Critical chance | 5% | Giới hạn 0–60%; damage crit ×1,5 |
| Regen | 0 HP/giây | Hồi liên tục trong combat, không hồi khi pause/shop |
| Pickup radius | 1,5 đơn vị | Tối đa 4 đơn vị |
| Reload duration modifier | 0% | Cộng modifier có dấu; thời gian cuối không dưới 50% thời gian gốc |
| Range bonus | 0% | Từ -30% đến +50% |

HP dùng float; UI hiển thị làm tròn lên. Damage sau giảm giáp tối thiểu 1. Sau khi nhận một hit: bất tử 0,35 giây với mọi nguồn damage, có nháy viền rõ. Không có xuyên giáp hoặc sát thương theo thời gian trong MVP.

Reload = `baseReload × max(0,5; 1 + tổng modifier thời gian)`. Ví dụ Yểm trợ +20% và một Dây mang súng -10% cho thời gian ×1,10, không phải ×0,90. Modifier kích thước băng cũng cộng có dấu trước khi nhân giá trị gốc.

Thứ tự damage: damage vũ khí theo tier → tổng damage modifier → critical → giáp mục tiêu → mất HP → sự kiện chết chỉ một lần. Một lần nổ chỉ trúng mỗi mục tiêu một lần. Projectile không xuyên chỉ trúng mục tiêu đầu tiên; hitscan chọn vật cản hoặc mục tiêu gần nhất trên tia.

### Ba lớp nhân vật

| Lớp | Vũ khí ban đầu | Đặc tính |
|---|---|---|
| Bộ binh | Súng trường | +15 HP tối đa, -5% tốc độ |
| Trinh sát | Tiểu liên | +15% tốc độ, +0,5 bán kính nhặt, -20 HP tối đa |
| Yểm trợ | Trung liên | +20% damage, +20% thời gian reload, -10% tốc độ |

Tên lớp mô tả vai trò gameplay, chưa gắn đơn vị quân đội hay quân phục cụ thể.

## 7. Vũ khí và build

- Tối đa **4 ô vũ khí**, được phép trùng loại. Đây là sự trừu tượng arcade cần người dùng duyệt; không diễn giải thành một người lính thực tế sử dụng đồng thời bốn khẩu súng.
- Mỗi vũ khí có cooldown, băng đạn và reload riêng; tự nạp khi băng hết. Không có kho đạn hữu hạn hoặc nhặt từng loại đạn.
- Hình ảnh có một súng chính trên nhân vật; trang bị phụ dùng mô hình đơn giản quanh nhân vật hoặc điểm phát hiệu ứng. Chọn cách thể hiện sau thử đọc hình ở combat spike, không tạo thêm lính AI.
- Auto aim ưu tiên địch gần nhất trong tầm và có đường bắn thoáng; giữ mục tiêu hợp lệ để tránh giật hướng. Kiểm tra lại trước khi bắn.
- Súng bắn thẳng không xuyên vật cản. Lựu đạn có thể bay qua vật cản nhưng blast chỉ trúng mục tiêu có đường nhìn từ tâm nổ.
- Cấp vũ khí I/II/III có damage ×1/1,35/1,8. Những thông số khác giữ nguyên ở MVP.
- Shop có nút ghép rõ ràng: hai bản cùng loại, cùng tier dưới III → một bản tier cao hơn, giải phóng một ô. Không ghép tự động hoặc vượt tier III.

| Vũ khí (tên chức năng tạm) | Damage cấp I | Nhịp bắn / băng / reload | Tầm | Vai trò |
|---|---:|---|---:|---|
| Súng trường | 12 | 0,35s / 12 / 1,5s | 10 | Cân bằng; hitscan có tracer |
| Tiểu liên | 5 | 0,12s / 24 / 1,4s | 6,5 | Nhiều hit, tầm gần |
| Trung liên | 8 | 0,15s / 40 / 2,8s | 8 | Bắn lâu, khoảng nghỉ reload dài |
| Súng tản đạn | 4 × 6 pellet | 0,85s / 5 / 2,0s | 4,5 | Damage gần, hình nón |
| Súng trường chính xác | 36 | 1,1s / 5 / 2,2s | 13 | Ít hit, damage cao |
| Bộ phóng lựu đạn | 22 / vụ nổ | 1,6s / 3 / 2,5s | 8 | Diện rộng bán kính 2; không self-damage |

Tên mẫu súng lịch sử và diện mạo phải được kiểm chứng theo hướng Quân đội Nhân dân Việt Nam, năm 1972, trước sản xuất asset. Bảng hiện tại là vai trò gameplay, không khẳng định sáu loại đều là trang bị tiêu chuẩn của đơn vị. Loại không có căn cứ phù hợp phải đổi cách thể hiện hoặc thay vũ khí giữ cùng vai trò rồi cập nhật spec.

### 18 vật phẩm bị động

Mỗi loại mua tối đa 2 bản; modifier cộng dồn rồi áp dụng giới hạn chỉ số. Không có ô inventory bị động hữu hạn trong MVP.

| ID | Tên tạm | Hiệu ứng mỗi bản |
|---|---|---|
| I01 | Túi cứu thương | +15 HP tối đa |
| I02 | Băng cá nhân | +0,3 HP/giây |
| I03 | Áo bảo hộ | +10 giáp, -3% tốc độ |
| I04 | Giày hành quân | +8% tốc độ |
| I05 | Dây mang súng | -10% thời gian reload |
| I06 | Bộ vệ sinh súng | +8% damage |
| I07 | Kính ngắm | +10% tầm, -3% tốc độ |
| I08 | Tay cầm | +10% attack speed |
| I09 | Túi đạn lớn | +20% kích thước băng, +5% thời gian reload |
| I10 | Sổ xạ kích | +5 điểm phần trăm crit |
| I11 | Ba lô tiếp tế | +0,5 bán kính nhặt |
| I12 | Thẻ hậu cần | +10% tiền từ pickup và thưởng hết đợt |
| I13 | Sổ huấn luyện | +10% XP |
| I14 | Trang bị nhẹ | +10% tốc độ, -10 HP tối đa |
| I15 | Nòng tăng cường | +15% damage, -8% attack speed |
| I16 | Cò nhạy | +15% attack speed, -8% damage |
| I17 | Túi quân y | +0,5 HP/giây, -5% damage |
| I18 | Dây đeo gọn | -15% thời gian reload, -10% kích thước băng |

Kích thước băng làm tròn xuống sau tổng modifier, tối thiểu 1. Giảm HP tối đa kẹp HP hiện tại theo trần mới, HP tối đa không dưới 1. Hiệu ứng chỉ tác động lần mua, không cộng lại khi tải UI. Vật phẩm không bán lại trong MVP.

## 8. XP, tiền và shop

XP và tiếp tế là **hai tài nguyên riêng** để tránh xung đột giữa lên cấp và tiêu tiền.

- Hạ địch thường: nhận trực tiếp 1 XP và rơi pickup 2 tiếp tế. Elite: 10 XP và 20 tiếp tế. Boss: thưởng kết quả, không dùng mua thêm đồ.
- XP lên cấp kế tiếp: `8 + 4 × (level - 1)`. Trừ ngưỡng, giữ XP dư; hỗ trợ lên nhiều cấp trong một đợt.
- Không ngắt combat khi lên cấp. Cuối đợt, mỗi cấp đạt được cho một lượt chọn 1 trong 3 nâng cấp khác nhau: +10 HP tối đa, +5% damage, +5% attack speed, +5 giáp, +5% tốc độ, +0,2 regen. Chưa có reroll nâng cấp ở MVP.
- Khi hết đợt, pickup chưa nhặt chuyển thành 50% tổng giá trị còn lại, làm tròn xuống một lần. XP không bị mất vì đã trao lúc hạ địch.
- Thưởng sống sót hết đợt: `10 + 2 × wave`. Không trả thưởng sống sót khi chết. Bonus thu nhập áp dụng lúc tạo thưởng/pickup, không áp dụng lần hai khi thu nhặt.
- Tiền dư giữ sang đợt sau, reset ở lượt mới. Không lãi suất hoặc tiền vĩnh viễn.

Ví tiền/pickup dùng số nguyên đơn vị nhỏ bằng 1/100 tiếp tế để tích lũy bonus thu nhập: pickup 2 với +10% là 220 đơn vị nhỏ. Không làm tròn xuống từng pickup. UI hiển thị phần nguyên có thể tiêu; tooltip có tối đa hai chữ số thập phân. Giá luôn là số nguyên tiếp tế và chuyển sang đơn vị nhỏ khi giao dịch. Riêng quyết toán 50% pickup còn lại làm tròn xuống đến số nguyên tiếp tế một lần như quy tắc trên; tiền đã nhặt không mất phần lẻ.

### Quy tắc shop

1. Shop có 4 ô hàng. Lần đầu vào shop không có hàng khóa: ít nhất 1 vũ khí và 1 passive, hai ô còn lại ngẫu nhiên. Khi điền lại, giữ hàng khóa rồi dùng ô không khóa để bổ sung loại còn thiếu trước khi roll ngẫu nhiên; nếu không đủ chỗ cho cả hai loại thì ưu tiên vũ khí. Bảo đảm hai loại không ghi đè hàng khóa; nếu không còn passive hợp lệ thì điền bằng vũ khí.
2. Vũ khí tier I luôn có thể xuất hiện; tier II từ đợt 5; tier III từ đợt 9. Trọng số I/II/III tương ứng 100/0/0, 75/25/0, 55/35/10.
3. Giá gốc vũ khí: 20/18/26/22/28/30 theo thứ tự bảng; tier ×1/2/4. Passive giá gốc 20, riêng I02/I12/I13/I17 là 28.
4. Giá cuối = `ceil(base × tierFactor × (1 + 0,06 × (wave - 1)))`. Passive có tierFactor = 1.
5. Reroll lần k trong shop, bắt đầu k=0: `4 + 2k + floor((wave-1)/3)`. Không giới hạn số lần nếu đủ tiền; bộ đếm reset ở shop kế tiếp.
6. Khóa từng ô hàng để giữ qua reroll và shop sau; không có phí khóa. Mua hàng làm ô trống đến lần reroll/chuyển shop sau. Sau mỗi giao dịch, mọi offer passive đã đạt stack cap phải bị xóa và bỏ khóa, không tự điền ngay; lúc mua luôn kiểm tra lại tính hợp lệ. Nếu cả bốn ô có hàng khóa hợp lệ, vô hiệu hóa reroll và không thu phí.
7. Khi đủ 4 vũ khí, không cho mua thêm; người chơi ghép hoặc bán trước. Bán vũ khí nhận 50% giá thực trả của bản đó, làm tròn xuống; vũ khí ban đầu có giá thực trả 0. Không được bán vũ khí cuối cùng.
8. Vũ khí ghép ghi tổng giá thực trả của hai đầu vào để tính giá bán; ghép không tạo thêm tiền.
9. Băng bó: một lần mỗi shop, giá 15, hồi 25 HP; không chiếm ô hàng, không dùng được khi HP đầy.
10. Nút mua không thể trừ tiền hai lần vì double-click; ví tiền không âm. Shop không có thời hạn.

RNG tách luồng cho spawn và shop, ghi seed vào kết quả để tái hiện các lựa chọn ngẫu nhiên; không hứa replay combat bit-identical giữa máy.

## 9. Địch, đợt và độ khó

Địch hiện được đặc tả theo vai trò chiến đấu của lực lượng tập kích hư cấu. Chưa gắn đơn vị lịch sử hoặc quốc tịch cho từng archetype; chọn trang phục/lực lượng phù hợp năm 1972 khi duyệt reference, không mặc định mọi địch đều là bộ binh Mỹ. Phần lớn áp lực đến từ truy đuổi và vị trí, không phải màn đạn dày đặc khó đọc.

| Vai trò | Hành vi | Cách đối phó |
|---|---|---|
| Áp sát | Đuổi theo, dừng ngắn rồi đánh gần | Di chuyển và giữ khoảng cách |
| Xung kích | Nhanh, ít HP, lao theo hướng đã báo | Né sang bên |
| Xạ thủ | Dừng lại ngắm 0,8s rồi bắn đạn chậm | Đọc hướng, dùng vật cản |
| Ném lựu đạn | Vòng báo nổ ít nhất 1,2s | Rời vòng cảnh báo |
| Lính nặng | Chậm, nhiều HP, không có khiên hướng trong MVP | Thả diều và tập trung damage |
| Yểm trợ | Tăng tốc địch gần, aura không cộng dồn | Ưu tiên hạ nguồn buff |

Elite là biến thể lính nặng lớn hơn, có hai đợt xung kích được báo trước; xuất hiện đợt 6 và 9. Boss là tổ hỏa lực hư cấu với ba chu kỳ: loạt đạn hình quạt, vùng nổ được báo, khoảng nghỉ; không gọi quân vô hạn.

| Đợt | Thời gian | Bổ sung nội dung |
|---|---:|---|
| 1–2 | 40s mỗi đợt | Áp sát → xung kích |
| 3–4 | 40s mỗi đợt | Xạ thủ; kiện tiếp tế ở đợt 3 |
| 5–6 | 50s mỗi đợt | Ném lựu đạn; elite và tiếp tế ở đợt 6 |
| 7–8 | 50s mỗi đợt | Lính nặng; tăng phối hợp |
| 9–11 | 60s mỗi đợt | Yểm trợ; elite và tiếp tế ở đợt 9 |
| 12 | 60s | Boss và ít địch hỗ trợ |

Tổng combat tối đa 600 giây. Thời gian mua đồ và lựa chọn nâng cấp tạo phần còn lại của lượt.

Spawn dùng ngân sách theo đợt, trọng số archetype và giới hạn sống đồng thời; chỉnh bằng dữ liệu. Bắt đầu thử trần 25/45/65/80 địch theo nhóm đợt 1–3/4–6/7–9/10–12. Không tăng damage, HP và số lượng cùng lúc mà không playtest. Giá trị HP/damage/spawn rate từng archetype là đầu ra của combat spike, chưa coi đã cân bằng ở tài liệu này.

Quy tắc bắt buộc: không spawn trong vật cản, không spawn gần người chơi dưới 6 đơn vị; đánh dấu vị trí ít nhất 0,6s trước khi kích hoạt. Nếu không có điểm hợp lệ, hoãn spawn; không ép sinh lên đầu người chơi hoặc dồn toàn bộ backlog trong một frame. Địch kẹt phải được phát hiện; không dịch chuyển sát người chơi.

Đạn địch không hitscan. Đạn thường và đòn gần tuân theo khoảng bất tử của người chơi. Giới hạn địch đánh xa cùng hoạt động phải được điều chỉnh để không lấp kín mọi đường né.

## 10. Arena và mục tiêu tiếp tế

- Arena cố định khoảng 34 × 20 đơn vị; cần thử theo camera và tốc độ nhân vật.
- Chủ đề: trạm tiếp tế hư cấu trong khu vực Trường Sơn năm 1972; bìa rừng, đường đất, bao cát, đá, hòm tiếp tế. Không dùng tên một trạm hoặc trận đánh có thật.
- Ít nhất hai đường vòng rộng qua mỗi cụm vật cản, không tạo ngõ cụt vô tình khóa người chơi.
- Bao cát/đá chặn di chuyển và đạn thẳng của cả hai bên; không có cơ chế cúi/nấp hoặc cover bonus riêng.
- Lá/cỏ trang trí không có collider. Tán cây che nhân vật phải làm mờ/cắt bớt; không che vòng báo nổ.
- Đợt 3, 6, 9 có một kiện xuất hiện ở giây 15 tại điểm đã xác nhận có đường đi. Bán kính thu hồi 1,5 đơn vị, tích lũy 3 giây ở trong vùng; ra ngoài giữ tiến độ, pause không tăng tiến độ.
- Kiện tồn tại đến hết đợt. Thu hồi một lần nhận 20 tiếp tế và hồi 10 HP, không vượt HP tối đa. Không tăng XP. Địch không phá kiện trong MVP.
- Bỏ qua kiện không làm thua hoặc giảm thưởng cơ bản. Nhiệm vụ phải tạo lựa chọn mạo hiểm, không ép người chơi đứng yên.

## 11. Hình ảnh, âm thanh và cách thể hiện lịch sử

Phong cách stylized nghiêm túc vừa phải: xanh ô-liu, nâu đất, điểm nhấn vàng cho tiếp tế; đạn địch và nguy hiểm có viền tương phản với nền. Không chỉ dùng màu để phân biệt phe hoặc hiểm họa: cần silhouette, biểu tượng và hình cảnh báo khác nhau.

Ưu tiên sản xuất asset: (1) nhân vật và hai địch đầu, (2) súng/đạn/hit effect, (3) vật cản và kiện, (4) các archetype còn lại và boss, (5) cây/cỏ/phông nền. Dùng blockout trong spike; không chi tiền sinh asset hoặc tải asset trả phí khi chưa được yêu cầu. Nếu sau này dùng Thrixel, đánh giá chi phí và duyệt riêng trước sinh model.

Animation tối thiểu: idle, chạy, bắn/recoil, bị trúng, chết; địch dùng chung rig nơi phù hợp. Không cần ragdoll. VFX giới hạn để không che đạn; âm thanh player/enemy tách biệt, có giới hạn số tiếng bắn đồng thời.

Nguyên tắc nội dung đề xuất: người lính được thể hiện như con người, không dùng miệt thị dân tộc; prototype không có dân thường làm mục tiêu, không khai thác thảm sát như phần thưởng. Không dùng hình ảnh tuyên truyền, khẩu hiệu, đơn vị hay nhân vật lịch sử cụ thể khi chưa chốt góc nhìn và kiểm chứng. Nhạc và âm thanh dùng tài nguyên có quyền sử dụng; không mặc định được dùng bài hát nổi tiếng cùng thời kỳ.

## 12. UI, lưu trữ và accessibility

HUD chỉ hiển thị HP, đợt/thời gian còn lại, cấp/XP, tiếp tế, bốn ô vũ khí với reload và tiến độ kiện khi ở gần. Thông báo tăng cấp ngắn, không che combat.

Shop hiển thị chỉ số trước/sau khi mua, tiền còn lại, giá reroll, khóa ô, ghép/bán, HP và băng bó. Mọi đánh đổi phải xuất hiện trong tooltip tiếng Việt.

Kết quả: thắng/thua, nhân vật, seed, đợt đạt tới, thời gian combat, damage, địch hạ, build cuối, nút thử lại/về menu. Số liệu phải lấy từ run thực, không ước tính từ thời gian.

Lưu local có version: cài đặt âm thanh, shake, damage numbers, ngôn ngữ và thống kê tổng. Không save giữa lượt trong MVP; thoát game mất lượt hiện tại và có cảnh báo. Save lỗi/cũ không được làm game kẹt ở menu; có fallback và log rõ.

UI tiếng Việt đủ dấu; font phải kiểm tra thực tế. Tối thiểu thử 1280×720 và 1920×1080. Cỡ chữ, cảnh báo nguy hiểm và nút cần rõ trên khung hình mục tiêu. Touch/mobile được tính đến khi thiết kế input nhưng không được tuyên bố hỗ trợ nếu chưa có build và đo thử.

## 13. Tiêu chí nghiệm thu gameplay MVP

- Chạy được vòng menu → đủ 12 đợt → kết quả → restart, không exception hoặc trạng thái tồn dư.
- Có thể chơi toàn bộ bằng di chuyển và auto-fire; manual aim không bắt buộc.
- Chết, hết giờ, hạ boss và pause cùng thời điểm không trao thưởng hoặc mở màn hình hai lần.
- Shop, ghép, bán, khóa, reroll, nhiều lượt lên cấp và stat caps có kiểm thử tự động.
- Vật cản chặn đúng đường bắn; pickup/kiện không ở điểm không thể đi đến.
- Ba lớp nhân vật và ít nhất ba hướng build khác nhau có thể hoàn tất lượt trong playtest nội bộ; không cần một món bắt buộc để thắng.
- Ít nhất 5 người thử mới: 4/5 hiểu điều khiển và cách bắt đầu đợt 2 mà không cần người hướng dẫn; ghi nguyên nhân chết khó hiểu để sửa. Đây là test nhỏ, không phải chứng minh thị trường.
- Mục tiêu 60 FPS tại 1080p trên máy PC tham chiếu sẽ được chốt ở P0, kiểm tra ở tải tối đa; chưa phải hiệu năng đã đo. Chi tiết benchmark và kỹ thuật trong PLAN.md.

## 14. Những điều cần duyệt

1. Giữ 3D stylized/2.5D hay chuyển sang 2D gần Brotato hơn?
2. Mức bám sát lịch sử và bộ reference art: hướng làm việc đã là Quân đội Nhân dân Việt Nam, năm 1972, nhiệm vụ hư cấu; chưa phải mô phỏng lịch sử chính xác.
3. PC trước có phù hợp, hay mobile phải là nền tảng chính?
4. Chấp nhận 4 ô vũ khí arcade, hay muốn một súng chính + các thiết bị hỗ trợ?

Các câu trả lời trên có thể sửa draft; chưa cài Unity, tạo project, lập trình, sinh asset hoặc xuất bản game trong lượt lập tài liệu này.
