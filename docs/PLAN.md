# PLAN — Top-down arena survivor trong bối cảnh chiến tranh Việt Nam

Trạng thái: bản đề xuất v0.2, ngày 25/09/2026. P0 và phần lớn P2 vertical slice đã triển khai/kiểm chứng trong Unity; bộ địch đã đủ 3 archetype thường và elite, còn art/audio đại diện và playthrough người thật đủ 6 wave. [SPEC.md](SPEC.md) là nguồn yêu cầu gameplay; PLAN mô tả cách thực hiện và chứng minh yêu cầu đó.

Cập nhật triển khai: người dùng đã duyệt Unity hiện có 6000.3.24f1 + URP, Windows offline và Git cục bộ không remote. Project `../TienTuyen` đã được khởi tạo; scene `CombatSpike.unity` là bản chơi P2 hiện tại. Bộ địch combat hiện đã có 3 địch thường (infantry, shooter, charger) và elite; trạng thái kiểm chứng mới nhất nằm ở README và `SETUP_STATUS.md`, thay thế trạng thái chưa tạo project của bản kế hoạch gốc. P2 còn art/audio đại diện và playtest cần hoàn tất trước khi đóng mốc.

## 1. Phạm vi và các quyết định cần chốt

Mục tiêu là một game có nhịp di chuyển–né tránh–tự bắn–nhặt tài nguyên–chọn build lấy cảm hứng từ Brotato, nhưng có hình ảnh, nội dung và cơ chế tiếp tế riêng. Không sao chép asset, nhân vật, giao diện hay dữ liệu cân bằng của game tham chiếu.

Các quyết định dưới đây giúp lập kế hoạch. Người dùng đã chọn “phe Việt Nam” và giao chọn giai đoạn; hướng làm việc là Quân đội Nhân dân Việt Nam, năm 1972. Các lựa chọn kỹ thuật khác chưa được người dùng xác nhận:

| Quyết định | Đề xuất để ước lượng | Khi phải chốt |
| --- | --- | --- |
| Nền tảng | Windows PC, offline, một người chơi | Trước P1 |
| Đồ họa | 3D stylized, gameplay phẳng 2.5D, camera orthographic, URP | Trước sản xuất asset |
| Editor | Unity 6 LTS; phiên bản patch chính xác TBD, kiểm tra tương thích trước khi khóa | P0 |
| Điều khiển | Bàn phím di chuyển; tự ngắm, tự bắn, tự nạp đạn | P0 |
| Bối cảnh | Quân đội Nhân dân Việt Nam; trạm tiếp tế hư cấu ở Trường Sơn, năm 1972 | Đã có hướng; reference quân phục, trang bị, đối phương cần duyệt trước art cuối |
| Quy mô | Một arena rừng, 12 wave, 3 lớp nhân vật, 6 định nghĩa vũ khí, 18 vật phẩm, 6 địch thường + 1 elite + 1 boss | P0 |
| Kinh doanh | Chưa quyết định; MVP không backend, quảng cáo, IAP | Sau đánh giá MVP |
| Máy hiệu năng | CPU/GPU/RAM và Windows build tham chiếu TBD | P0; không tuyên bố cấu hình tối thiểu trước đo |

“Quân đội Nhân dân Việt Nam” là cách diễn giải làm việc đã thông báo cho cụm “phe Việt Nam”, có thể sửa nếu người dùng muốn phía khác. Graybox dùng tên vai trò; nội dung có thật cần kiểm tra nguồn và duyệt riêng. P0 bổ sung bảng reference năm 1972 cho quân phục, súng và lực lượng đối phương; loại hoặc thay hình ảnh không phù hợp. Câu chuyện giữ trạm/chờ đoàn xe không thêm cơ chế HP trạm hay hộ tống AI. Nếu đổi sang 2D/pixel art hoặc mobile-first, phải ước lượng lại phần art, UI, rendering và kiểm thử trước P1.

## 2. Chiến lược giao hàng

Đường găng: P0 chốt hướng → P1 combat spike 3 wave → P2 vertical slice 6 wave → P3 MVP 12 wave → P4 kiểm thử và bàn giao. Không mở rộng nội dung khi combat hoặc vòng mua sắm chưa đạt.

| Mốc | Đầu vào/phụ thuộc | Sản phẩm cụ thể | Điều kiện vượt mốc | Ước lượng ngày công |
| --- | --- | --- | --- | --- |
| P0 — Chốt hướng | Phản hồi trên SPEC/PLAN | Brief được duyệt; engine/platform/UI stack; reference PC; danh sách asset và quyền sử dụng; backlog ưu tiên | Không còn quyết định có thể buộc làm lại kiến trúc hoặc toàn bộ art | 1–2 |
| P1 — Combat spike | P0 | Arena graybox; 1 lớp; 2 vũ khí; 2 địch; 3 wave; bắn/nạp đạn/LOS; nhặt tài nguyên; pause/death/restart; chưa có shop hoặc kiện tiếp tế | **Đã triển khai và kiểm chứng** bằng 13 Edit Mode + 5 Play Mode tests, camera Play Mode và Windows standalone smoke. Full human 3-wave playthrough và cân bằng vẫn mở | 4–6 |
| P2 — Vertical slice | P1 đạt combat gate | 6 wave; 1 lớp; 3 vũ khí; 6 vật phẩm; 3 địch thường và elite; tối đa 4 slot; upgrade → shop; một sự kiện supply tại wave 3; một góc art/audio hoàn thiện đại diện | Vòng lặp từ menu đến kết thúc 6 wave chạy trên Windows build; ít nhất 2 build khác cách chơi; ngân sách hiệu năng có capture ban đầu | 7–10 |
| P3 — MVP nội dung | P2 đạt loop gate | 12 wave; đủ 3 lớp, 6 vũ khí, 18 vật phẩm, 6 địch thường, elite, boss; supply wave 3/6/9; toàn arena đồng nhất; tutorial ngắn; settings và kết quả | Có thể thắng/thua/chơi lại bằng cả 3 lớp; mọi nội dung thực sự xuất hiện và dùng được; không thiếu prefab hoặc âm thanh bắt buộc | 8–12 |
| P4 — Ổn định, cân bằng | P3 feature complete | Bộ test, sửa lỗi, pass accessibility, profiling, cân bằng, gói Windows nội bộ, hướng dẫn build | Các acceptance bên dưới đạt; không lỗi chặn; 3 lượt chơi hoàn chỉnh liên tiếp ổn định trên máy tham chiếu | 5–8 |

Tổng cơ sở: 25–38 ngày công, cộng dự phòng 20–30% khoảng 5–12 ngày công → khoảng 30–50 ngày công. Đây là ước lượng định hướng, không cam kết thời hạn. Giả định một người có kinh nghiệm Unity, asset stylized đơn giản hoặc có sẵn quyền sử dụng, không voice acting hay animation điện ảnh. Một ngày công là nỗ lực lao động, không đồng nghĩa một ngày lịch; custom art và nghiên cứu lịch sử chuyên sâu cần ước lượng riêng. Đánh giá lại sau P1 và P2.

### Công việc có thể chạy song song

- Sau P0: thiết kế graybox/combat và moodboard + kiểm kê quyền sử dụng asset có thể chạy độc lập.
- Sau khi khóa data schema ở P1: content authoring, UI trình bày và test công thức có thể chạy song song với gameplay. Giao rõ mỗi người phụ trách scene/prefab nào; tránh cùng sửa YAML scene.
- Khi gameplay interface đổi: cập nhật hợp đồng dữ liệu trước, tích hợp theo từng nhánh nhỏ; người phụ trách tích hợp chạy lại test và build trước nhập nhánh.
- Chỉ bắt đầu sản xuất toàn bộ 18 items sau khi pipeline một item hoàn chỉnh đã được test.

## 3. Kiến trúc đề xuất

Giữ C# + MonoBehaviour đủ đơn giản để debug. Không dùng ECS/DOTS, DI framework, netcode, Addressables hoặc backend chỉ vì có sẵn. Chỉ thêm dependency khi có nhu cầu đo được. URP lấy từ template phù hợp; Input System và Test Framework dùng phiên bản tương thích với Editor đã chọn, không khóa phiên bản theo trang tài liệu minh họa.

Input Actions tách ý định người chơi khỏi thiết bị; đây là cách tổ chức phù hợp cho việc thêm gamepad/touch sau này. [Unity Input System — Actions](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.11/manual/Actions.html)

| Module | Trách nhiệm và hợp đồng | Không được sở hữu |
| --- | --- | --- |
| RunFlow | State machine; khởi tạo/huỷ run; thứ tự wave → upgrade → shop; thắng/thua | Công thức damage, giá shop |
| Player | Input intent, movement phẳng, health, nhận sát thương, invulnerability theo SPEC | Spawn, inventory UI |
| Combat | Weapon runtime, targeting/LOS, cooldown/reload, projectile, damage resolver | Tự ghi currency hoặc sửa ScriptableObject |
| Enemies | Vai trò AI, movement, attack telegraph, truy cập target registry | Mỗi địch tự tìm toàn scene mỗi frame |
| Waves | Wave clock, spawn budget, enemy cap, event elite/boss và optional objective | Trực tiếp mở UI hoặc trả thưởng hai lần |
| Progression | XP, lựa chọn upgrade, stat aggregation, inventory 4 weapon slots và passive items | Hiển thị text hoặc xử lý input thiết bị |
| Economy | Currency, giá, reroll, buy/sell theo SPEC; giao dịch nguyên tử | GameObject hoặc animation |
| Presentation | HUD, menu, shop, audio/VFX, camera feedback | Tự sửa stat hoặc health ngoài command hợp lệ |
| Infrastructure | Pool, clocks, RNG có seed, settings save, validation, debug counters | Luật thắng/thua |

Hướng phụ thuộc: presentation/input gửi command cho runtime; runtime phát thông báo kết quả; UI đọc snapshot để hiển thị. Logic công thức và giao dịch là C# độc lập để test nhanh; MonoBehaviour làm adapter cho scene, physics và Unity lifecycle. Ưu tiên reference được gán rõ hoặc bootstrap theo run, không để một singleton toàn cục quản mọi thứ.

### State và vòng đời

Luồng cơ bản: Menu → RunSetup → WaveActive → WaveResolve → UpgradeChoice → Shop → WaveActive tiếp theo → Victory/Defeat → Results. Pause là lớp phủ giữ trạng thái trước đó; chỉ được áp dụng cho các state cho phép. Nhánh final-wave, tiêu diệt boss, hết giờ và người chơi chết đồng thời phải tuân thủ thứ tự ưu tiên trong SPEC, có test riêng; không để callback nào đến trước tự quyết định kết quả.

- Gameplay clock dừng trong pause, shop, upgrade và results. UI animation có thể dùng unscaled time; chặn input combat xuyên qua menu.
- WaveResolve chỉ chạy một lần: ngừng spawn/attack, xử lý reward và pickup còn lại theo SPEC, dọn entity; không để coroutine cũ bắn hoặc trả thưởng ở wave sau.
- Restart tạo RunState mới, seed mới hoặc seed debug được chọn; giải phóng event subscription và pool đang dùng; khôi phục time scale/input/audio. Settings được giữ, stat/currency/inventory của run cũ không được giữ.
- MVP chỉ lưu settings và các dữ liệu kết quả được SPEC yêu cầu; không có resume giữa run. Không lưu runtime vào asset thiết kế.

### Dữ liệu thiết kế và dữ liệu khi chơi

ScriptableObject phù hợp chứa các định nghĩa dùng chung; runtime state được tạo riêng cho từng run và từng entity. Đây là quyết định kiến trúc của dự án, dựa trên khả năng lưu dữ liệu dạng asset của Unity; không dùng ScriptableObject asset như cơ chế save-game ở standalone Player. [Unity — ScriptableObject](https://docs.unity3d.com/6000.0/Documentation/Manual/class-ScriptableObject.html)

| Asset thiết kế | Nội dung bất biến khi chơi | Runtime riêng |
| --- | --- | --- |
| CharacterDefinition | ID, base stats, trait, starter weapon, presentation refs | Health, stats sau modifiers, XP/level |
| WeaponDefinition | ID, attack type, damage, cooldown, magazine, reload, range, projectile/presentation refs | Level/tier, cooldown còn lại, rounds trong magazine, target |
| ItemDefinition | ID, modifiers, rarity/shop weight, price, stack cap, icon | Số lượng sở hữu; modifier instances |
| EnemyDefinition | ID, stats, AI role, attack config, loot, prefab | Health, AI state, attack timer, vị trí |
| WaveDefinition | Duration, spawn composition/budget, max alive, elite/boss/objective config | Elapsed time, spawned count, pending budget, completion flag |
| ArenaDefinition | Bounds, spawn regions, obstacle/spawn rules, scene/prefab refs | Active entities, occupancy/path cache |
| Economy/UpgradeTables | Price/probability curves, eligible choices, stat caps | Currency, shop offers, reroll count, pending level-ups |

Mỗi định nghĩa có stable ID; validator bắt ID trùng, reference rỗng, thời lượng/giá âm, tổng weight bằng 0, slot vượt 4 và wave không hợp lệ. Save settings dùng schema version. JSON save không chứa tham chiếu trực tiếp GameObject/asset; chỉ ID và giá trị cần thiết.

### Movement, targeting và vật cản

- Gameplay trên mặt phẳng XZ; collider/physics 3D thống nhất, không trộn Physics2D. Mỗi layer có mục đích: player, enemy, player projectile, enemy projectile, obstacle, pickup.
- P1 kiểm chứng character movement + collision sweep và AI steering qua arena thật. Vật cản tĩnh phải có đường vòng; không dùng chỉ vector lao thẳng vào người chơi rồi chấp nhận kẹt.
- Nếu đường vòng không ổn: chọn một giải pháp sau spike — shared coarse-grid flow field cho đám đông hoặc AI Navigation với budget cập nhật đường đi. Không xây cả hai, không thêm dynamic obstacle/destruction trong MVP.
- Target registry lọc theo range và loại địch, sau đó kiểm tra LOS bằng layer mask; tái chọn mục tiêu theo cadence thay vì toàn bộ vũ khí quét mọi frame.
- Hitscan/đạn tốc độ cao phải kiểm tra đoạn đường bay hoặc raycast phù hợp để không xuyên collider ở frame thấp. Collider của projectile không chặn movement.
- Camera/foliage không che đường đạn và telegraph; cây trang trí không được âm thầm trở thành cover nếu hình ảnh không nói rõ.

### Pooling và hiệu năng theo thiết kế

Pool enemy, projectile, pickup và hit VFX; reset đầy đủ health/timer/target/velocity/trail/event khi tái sử dụng. Unity có `ObjectPool<T>` tích hợp; giới hạn số entity đang hoạt động vẫn là trách nhiệm của gameplay, không suy ra từ kích thước pool. [Unity — ObjectPool](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Pool.ObjectPool_1.html)

Prewarm theo stress scene đã đo; tránh Instantiate/Destroy liên tục trong combat. Có counter active/pooled/high-water mark và chính sách khi đầy: hoãn spawn enemy; gộp pickup; bỏ bớt VFX trang trí. Không âm thầm xoá projectile gây damage chỉ vì thiếu pool, làm kết quả combat sai.

## 4. Kiểm thử và tiêu chí nghiệm thu

Edit Mode cho công thức/data validation; Play Mode cho lifecycle/physics/state; standalone Windows cho input, UI, build và hiệu năng. Unity Test Framework hỗ trợ cả Edit Mode và Play Mode, kể cả Play Mode trên Player. Phiên bản package sẽ được chọn cùng Editor ở P0. [Unity — Automated tests](https://unity.com/how-to/automated-tests-unity-test-framework)

| Nhóm | Trường hợp bắt buộc | Tiêu chí đạt |
| --- | --- | --- |
| Combat | Damage/armor/crit, tốc bắn, auto reload, range, weapon tier/slot | Kết quả đúng công thức SPEC; stat cap hợp lệ; không chia 0 hoặc damage âm; magazine/reload độc lập mỗi slot |
| LOS/physics | Player/địch ở hai phía cover; đạn tốc độ cao; frame 30/60/120; target chết/mất LOS | Không bắn xuyên cover ngoài cơ chế được SPEC cho phép; không target object đã về pool; không xuyên collider do một frame dài |
| Spawn/pathing | 100 seed debug; player sát biên; chướng ngại bao quanh vùng spawn; cap đạt trần | Không spawn trong collider hoặc vùng an toàn player; số alive không vượt cap; spawn bất thành không tạo vòng lặp vô hạn; luôn có đường hợp lệ hoặc chọn vị trí khác |
| Waves | 12 wave 40/50/60 giây, mỗi nhóm 4; pause giữa wave; kill cuối và hết giờ cùng frame | Clock đúng, không double resolve; boss/elite đúng lịch SPEC; chuyển state đúng một lần |
| Economy | Không đủ tiền; double click; reroll; slot đầy; bán/mua/stack; chuyển shop | Currency không âm; giao dịch all-or-nothing; không dup đồ; tier/stack/giá đúng SPEC; một offer không thể mua hai lần |
| Upgrade | Nhiều level-up cùng wave; option không còn hợp lệ; duplicate; hết pool lựa chọn | Mỗi pending upgrade được xử lý đúng một lần; không softlock; fallback minh bạch |
| Supply | Hoàn thành/bỏ qua ở wave 3/6/9; người chơi chết khi nhận thưởng; hết giờ khi tiến độ hoàn tất | Optional thực sự: bỏ qua không chặn wave; reward cấp một lần; quy tắc đồng thời theo SPEC |
| Pool | Spawn/despawn 1.000 vòng; restart khi projectile còn bay; double release | Không giữ health/target/trail cũ, không event leak, không damage từ run cũ, không double release |
| Pause/restart | Pause khi reload/telegraph; thoát shop; focus lost; restart 20 lần | Timer/gameplay dừng nhất quán; input UI không lọt vào combat; không kẹt timeScale=0; run mới không thừa entity/currency |
| Content | Từng class/weapon/item/enemy, prefab references và text tiếng Việt | Đủ danh mục SPEC; không missing script/material; tooltip khớp tác dụng; không lỗi dấu tiếng Việt |
| UX/readability | 1280×720, 1366×768 và 1920×1080; combat dày; tắt screen shake; không phân biệt đỏ/xanh | HUD không bị cắt; nguy hiểm có hình dạng/telegraph ngoài màu; người chơi nhìn rõ bản thân và đường né |
| Build | Clean checkout/import; test; Windows build; offline launch; settings corrupt | Build tái tạo được theo README; không exception; settings hỏng có fallback; không cần tài khoản/network để chơi |

Các seed chỉ tái hiện RNG spawn/loot/shop để debug, không hứa simulation physics bit-for-bit deterministic. Mỗi lỗi lưu seed, version build, wave, class/loadout và bước tái hiện.

### Cân bằng và playtest

- P1: 3–5 lượt quan sát, kiểm tra người chơi hiểu auto fire/reload và cover; ghi lý do chết, chỗ kẹt, lúc mất dấu nhân vật. Đây là phát hiện vấn đề ban đầu, không phải nghiên cứu thống kê. Supply được kiểm chứng ở P2.
- P2: kiểm chứng ít nhất 2 hướng build có khác biệt quan sát được; một lựa chọn shop không luôn lấn át các lựa chọn khác. Log cục bộ damage taken/dealt, thu nhập, mua/reroll, tài nguyên còn lại và thời điểm chết.
- P3/P4: 5–8 người thử nếu có; mỗi lớp có thể thắng bởi người đã hiểu luật; người mới hiểu core loop trong khoảng 2 phút. Mục tiêu win-rate chỉ chốt sau có mẫu playtest đủ phù hợp; không dùng bot để thay thế đánh giá cảm giác chơi.
- Economy simulation kiểm tra khả năng gặp offer mua được sau wave đầu và midgame, không tạo vòng bán-mua sinh lời vô hạn; exact price curve điều chỉnh bằng log, không theo cảm giác đơn lẻ.

## 5. Ngân sách hiệu năng đề xuất

Các số dưới đây là mục tiêu kỹ thuật tạm thời, chưa phải kết quả benchmark hoặc cấu hình máy tối thiểu. Khóa reference PC tại P0, kiểm tra P1 rồi cập nhật có ghi lý do.

| Hạng mục | Mục tiêu ban đầu |
| --- | --- |
| Độ phân giải/framerate | 1920×1080, 60 FPS, frame budget 16,67 ms |
| Stress scene | 150 enemy alive, 200 projectile đồng thời, 150 pickup; 4 vũ khí người chơi; boss + telegraph; arena art đại diện. Đây là test headroom cố ý vượt cap gameplay 80 địch của SPEC, không thay đổi cap trong game |
| CPU/GPU | Gameplay main-thread khoảng ≤ 5 ms; tổng CPU và GPU mỗi bên ≤ 16,67 ms ở p95 steady combat; đo riêng, không cộng CPU+GPU |
| Stutter | Release-build p99 frame time ≤ 25 ms ở stress mục tiêu; không spike combat lặp lại > 50 ms sau warm-up |
| Managed allocation | Hot path combat 0 B/frame sau warm-up; UI chuyển state có allocation hữu hạn, không tăng theo số lần chơi lại |
| Bộ nhớ | Working set mục tiêu ≤ 1 GB trên Windows; sau 20 restart không tăng đơn điệu, so snapshot sau warm-up/thu gom tương đương |
| Thời gian đo | Warm-up 30 giây; capture 120 giây stress; thêm 3 run đủ 12 wave |

Profiler nối vào Development Build trên máy đích để tìm nguyên nhân; xác nhận frame time lần cuối ở non-Development Build vì profiling có overhead. Lưu cấu hình máy, quality level, resolution, build hash, seed và capture cùng kết quả, không dùng FPS trong Editor làm bằng chứng đạt. [Unity — Profiling on target device](https://docs.unity3d.com/6000.0/Documentation/Manual/profiling-target-device.html), [Unity — Profiler overhead](https://docs.unity3d.com/cn/6000.0/ScriptReference/Profiling.Profiler.html)

Nếu không đạt: đo bottleneck → giảm chi phí tương ứng (cadence AI/LOS, shader/overdraw/shadow, VFX/audio voices, pickup count) → kiểm tra lại gameplay. Chỉ xem xét Jobs/ECS khi cách đơn giản đã đo và không đủ; đó là thay đổi phạm vi cần ước lượng lại.

## 6. Rủi ro và thứ tự cắt phạm vi

| Rủi ro | Dấu hiệu sớm | Giảm thiểu / phương án cắt |
| --- | --- | --- |
| Không vui dù đủ nội dung | P1 người chơi chỉ đi vòng, auto fire không tạo quyết định | Đổi telegraph, khoảng trống, vai trò địch và supply trước; không chữa bằng thêm 20 loại súng |
| Cover làm AI kẹt | Enemies tụ lại trước cùng vật cản | Gate pathing ở P1, sửa layout/thuật toán; không chờ P4 |
| Bối cảnh nhạy cảm hoặc thiếu nhất quán | Quân phục/vũ khí/thời kỳ lẫn lộn | Duyệt content bible trước art final, dùng nhãn hư cấu ở prototype, kiểm tra nguồn cho chi tiết thật |
| Art che gameplay | Lá cây/VFX che bullet và telegraph | Ưu tiên silhouette, ground readability; giảm foliage/VFX trước tăng độ sáng toàn cảnh |
| Build combinatorics | Item modifier tương tác khó giải thích hoặc khó test | Giữ modifiers có thứ tự áp dụng rõ; loại proc chain/đệ quy và active ability phức tạp khỏi MVP |
| Bùng số entity | FPS rơi ở nửa cuối run | Cap explicit, pooling, gộp pickup, đo stress sớm |
| Quá tải art/content | P2 chưa có pipeline asset ổn | Dùng chung skeleton/animation, giảm biến thể trang trí; không nhân số map |
| Thiếu thời gian | Ước lượng P2 vượt ngân sách | Cắt cosmetics, postprocess, camera shake cầu kỳ trước; sau đó đề xuất giảm catalog và cần duyệt lại SPEC |

Không tự cắt điều kiện an toàn gameplay: LOS rõ, spawn công bằng, pause/restart đúng, chống dup currency và thông báo chết. Nếu cần giảm 3 lớp/6 vũ khí/18 items hoặc 12 wave thì đó là thay đổi scope có ghi nhận, không được báo đã hoàn thành MVP ban đầu.

Ngoài MVP: multiplayer/co-op, campaign lịch sử, nhiều bản đồ, bản đồ procedural, công sự phá huỷ được, đạn hữu hạn cần loot, skill tree lâu dài, meta progression cộng chỉ số, vehicle điều khiển, voice acting, account/cloud, ads/IAP và phát hành công khai.

## 7. Kiểm tra mobile/Web trong tương lai

Không build hoặc publish mobile/Web ở giai đoạn tài liệu và không coi đó là tiêu chí bàn giao MVP Windows. Kiến trúc input và UI không khóa cứng thiết bị để thử sau.

- Touch prototype: virtual stick bên trái; auto aim/fire/reload giữ nguyên; nút pause và UI shop không che vùng né; kiểm tra dead zone, safe area, chữ và tap target trên máy thật. Chưa thêm manual-aim stick hoặc nút active skill ngoài SPEC.
- Mobile: chọn ít nhất một máy tham chiếu, kiểm tra nhiệt/pin và FPS sau 15 phút; budget entity/shadow/VFX riêng, không suy ra từ Windows.
- Web: kiểm tra startup/download size, memory, tab mất focus, audio unlock, lưu settings và input capture trên browser mục tiêu; không giả định bộ test Windows là đủ.
- Sau P2 chỉ lập bản estimate cho nhánh port nếu người dùng chọn; chọn module/package và phiên bản dựa trên nền tảng lúc đó. Không triển khai backend hay hosting tự động.

## 8. Bàn giao và định nghĩa hoàn thành

Mỗi mốc có executable nội bộ tương ứng (khi bước sang triển khai), changelog ngắn, known issues, kết quả test và clip/capture cho tính năng mới. MVP cuối có project source + `.meta`, danh mục license asset, README mở/build/run, bảng content thực tế, test report và performance report trên reference PC.

Một tính năng chỉ “done” khi chạy trong standalone build, đạt acceptance liên quan, thông số có nguồn dữ liệu duy nhất, không phá pause/restart và có phản hồi hình/âm/UI đủ hiểu. “Đã viết code” hoặc “không có lỗi compile” chưa đủ.

Bước tiếp theo sau khi duyệt tài liệu: chốt nền tảng, kiểu đồ họa và reference nội dung theo hướng năm 1972 ở P0; sau đó mới bắt đầu thiết lập project và combat spike 3 wave. Không cần cam kết toàn bộ catalog trước khi spike chứng minh vòng chiến đấu đáng chơi.
