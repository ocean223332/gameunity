# Graphics Upgrade — kế hoạch URP

Ngày cập nhật: 15/10/2026  
Phạm vi: Unity `6000.3.24f1`, URP package `17.3.0`, game third-person shooter Tiền Tuyến.

Các con số về máy và cấu hình dự án là số đã kiểm tra. Mức effort, RAM và VRAM không có benchmark của project được ghi là **ước lượng**.

## 1. Quyết định đã chốt

| Chủ đề | Quyết định |
|---|---|
| Pipeline | Ở lại **URP**, làm “đẹp nhất khi dùng URP kể cả có phải refactor code”. Không có nhánh HDRP. |
| Mục tiêu hình ảnh | Bốn ảnh tham chiếu của người dùng (§2). Kết quả là ưu tiên hàng đầu; công sức refactor không phải lý do để bỏ một bước. |
| Biome | Thay rừng nhiệt đới (chuối, cọ, monstera, tre) bằng **rừng thưa thông ba lá (_Pinus kesiya_) + đồng cỏ** cao nguyên Trường Sơn, có cây non lá rộng, hoa dại và đá. |
| Ánh sáng / thời tiết | ScriptableObject `LightingPreset`, hai preset: **nắng/golden** và **u ám sương mù**. Mỗi run chọn một preset (mặc định ngẫu nhiên). Ánh sáng realtime. |
| Địa hình | **Không dùng TerrainData / Terrain Tools.** Giữ mesh + shader `EnvGround`; khi cần đổi hình dáng thì nướng heightmap và để `GroundHeight` đọc heightmap đó (§5). |
| Asset | Asset nào cũng được nếu có credit trong README (coursework phi lợi nhuận). Megascans không có sẵn; người dùng sẽ mua một số (§7). |
| Hiệu năng | 40–60 FPS chơi được, đo ở 1080p. Sàn cứng **1 GB free RAM**; dưới 2 GB thì ghi là swap-affected, không chặn. VRAM 6 GB. |
| Combat | Mỗi bước có capture combat đông, enemy đứng trong cỏ và một lần kiểm tra grayscale: player, enemy và cảnh báo phải còn đọc được. |

Máy tham chiếu: 16 GB RAM, RTX 3060 Laptop GPU 6 GB VRAM (kèm Intel Iris Xe).

### Vì sao không HDRP

Project có tám shader `Env*.shader` + `EnvCommon.hlsl` viết tay cho URP, `BloodSurface.shadergraph` (URP decal), `DecalRendererFeature`, camera stack và `UniversalAdditionalCameraData` trong C#, Volume profile tạo lúc runtime, và test kiểm tra URP (`Tests/Editor/FoundationTests.cs:73-96`). Không có bước chuyển URP → HDRP nào tự giữ được những thứ này ([H1]–[H6]). Những gì ảnh tham chiếu cần (sương theo độ cao, tia nắng có bóng, cỏ dày, asset scan) đều làm được trong URP 17.3 (§4).

## 2. Ảnh tham chiếu và những gì còn thiếu

Ảnh nằm trong `output/research/graphics/refs/` (gitignored).

| Ảnh | Nội dung | Còn thiếu so với game hiện tại |
|---|---|---|
| `ref1_misty_mountains_soldier` | Người lính nhìn từ sau lưng trên sườn cỏ cao nguyên; các dãy núi lớp lớp nhạt dần vào sương; mây nằm trong thung; đá rải rác; grade điện ảnh hơi nhạt màu. | Địa hình tầm trung 100 m–2 km (đồi cỏ, sống núi) giữa vùng chơi và backdrop; mây thung lũng dày nhiều lớp (`EnvMountainCloud` hiện mỏng). Cỏ ở xa do shader mặt đất vẽ (màu, noise, normal theo pixel), không dùng lá cỏ thật. |
| `ref2_pine_clearing_boulder` | Bãi trống trong rừng lá kim, cỏ dày lay gió, tảng đá scan phủ rêu, tia nắng ngược sáng xuyên sương. | Cỏ dày bằng GPU; đá scan lớn có rêu và chân đá hoà vào đất; tia nắng thật bị tán cây che (hiện là card cộng màu không có bóng). |
| `ref3_overcast_pine_road_wreck` | Trời u ám, sương dày, rừng lá kim thưa, đường đất bùn có vệt bánh xe, xác xe bọc thép gỉ, suối, đá, dây điện. | Preset u ám/sương; vũng nước phản chiếu trên đường (đường và vệt bánh đã có trong `EnvGround`); suối chảy theo spline; xác xe đúng thời kỳ (M113 hoặc PT-76). |
| `ref4_sunny_pine_meadow` | Đồng cỏ nắng, thông cao xen cây non lá rộng, hoa dại hồng, đá tối màu rải rác, mây tích, sương xa, núi nền. | Nhiều loại cỏ + cỏ ba lá + cụm hoa theo mảng noise, màu khác nhau từng instance; mật độ rừng thưa có khoảng trống nhìn ra xa; bầu trời có mây tích. |

Ba trong bốn ảnh là rừng lá kim, ảnh còn lại là đồi cỏ trống. Điểm chung là **đồng cỏ thoáng, cây thưa hoặc mọc thành cụm, tầm nhìn xa**: ngược hẳn với rừng nhiệt đới dày hiện nay (`EnvironmentFlora.cs:51-56`; khoảng cách cây ở dải rừng là 5.2 m, `EnvironmentFlora.cs:62`). Thông ba lá có ở cao nguyên Trường Sơn nên hợp bối cảnh, và hợp ref1/ref4 hơn vân sam (ref2/ref3 mang dáng châu Âu). Đồng cỏ thoáng còn ít lớp lá alpha chồng lên nhau hơn rừng rậm: rẻ hơn cho GPU và dễ nhìn thấy enemy hơn.

## 3. Hiện trạng render (đã kiểm tra trong code)

- **Thế giới được dựng lúc runtime.** `CombatEnvironment.Build` tạo ánh sáng, sky, post, mặt đất, trạm, rừng, backdrop và không khí khi bắt đầu (`CombatEnvironment.cs:31-44`). Vì thế hiện **không dùng được** lightmap, APV, reflection probe nướng sẵn hay occlusion culling.
- **Ánh sáng viết cứng.** Mặt trời, ambient Trilight, fog, `_EnvHaze`, màu sky và grade đều là hằng số trong `CombatEnvironment.SetupLighting/SetupSky/SetupPostProcessing` (`CombatEnvironment.cs:48-140`). Không có reflection probe; ambient là ba màu phẳng (`CombatEnvironment.cs:66-72`).
- **Tách sương cảnh và sương nhân vật.** `_EnvHaze` chỉ phủ cảnh vật, còn fog toàn cục chạm tới nhân vật được giữ nhẹ (`CombatEnvironment.cs:83-86`). Đây là cơ chế giữ enemy đọc được, cần giữ trong mọi preset.
- **Anti-aliasing.** Camera dùng SMAA (`CombatThirdPersonCamera.cs:92`), MSAA tắt (`PC_RPAsset.asset:28`), rendering path Forward+ (`PC_Renderer.asset:52`). Cỏ và lá alpha-clip sẽ nhấp nháy khi dày lên.
- **Motion vector.** `EnvLeaf` chỉ có pass Forward, ShadowCaster, DepthOnly, DepthNormals (`EnvLeaf.shader:84,190,230,258`), không có MotionVectors, dù gió dịch đỉnh (`EnvLeaf.shader:72`). `FloraScatter` vẽ với `ForceNoMotion` (`FloraScatter.cs:225`). Bật TAA/STP lúc này sẽ làm lá bị nhòe.
- **LOD.** URP asset bật LOD cross-fade (`PC_RPAsset.asset:33`), nhưng `FloraScatter` tự chọn LOD và chuyển cứng (`FloraScatter.cs:186-189`): cây sẽ “pop”.
- **Grade.** HDR bật, nhưng Color Grading Mode = LDR (`PC_RPAsset.asset:80`). ACES, contrast 10, saturation 10, exposure .5 (`CombatEnvironment.cs:114-123`).
- **Bóng.** Main light 2048, 4 cascade, 50 m (`PC_RPAsset.asset:46,57-58`), soft shadow cao. SSAO nửa độ phân giải (`PC_Renderer.asset`, feature ScreenSpaceAmbientOcclusion).
- **Tia nắng.** Card cộng màu `EnvLightShaft` đặt tay (`EnvironmentAtmosphere.cs:51`), không bị tán cây che. Mist/smoke/dust là ParticleSystem (`EnvironmentAtmosphere.cs:20-41`).
- **Cỏ.** Vài prefab `GrassClump`/`GrassTall` trên các bụi cỏ ở sân (`EnvironmentFlora.cs:150-165`), vẽ instanced qua `FloraScatter` (`Graphics.RenderMeshInstanced`, lọc theo ô 20 m trên CPU).
- **Sky.** `EnvSky` procedural với `_CloudCover` (`CombatEnvironment.cs:89-103`).

## 4. Kỹ thuật chọn cho từng phần

### Nền tảng render
- **AA:** làm spike so sánh hai đường rồi chọn theo capture chuyển động và GPU ms:
  - (a) **STP**, upscaler/TAA có sẵn của URP 6 ([R1], [R2]). Cần thêm pass MotionVectors cho shader lá/cỏ, tính gió của frame trước, và bỏ `ForceNoMotion`.
  - (b) **MSAA 4x + AlphaToMask** trên Forward+.
- **LOD cross-fade có dither** cho từng instance trong `FloraScatter`.
- **Grade HDR:** chuyển Color Grading Mode sang HDR rồi chỉnh lại các số trong preset.
- **Bóng:** thử main light 4096 (khoảng +48 MB VRAM so với 2048).
- **Ambient:** ambient lấy từ sky và một reflection probe render một lần mỗi khi preset đổi (realtime probe, `RenderProbe` theo yêu cầu).

### Lighting preset
`LightingPreset` (ScriptableObject) gồm: hướng/màu/cường độ mặt trời, ambient, fog nhân vật, `_EnvHaze`, tham số sky và mây, mật độ volumetric, Volume profile. `CombatEnvironment` đọc preset thay cho hằng số. Hai preset ban đầu: nắng/golden (ref2, ref4) và u ám sương (ref3). Mỗi run chọn một (mặc định ngẫu nhiên).

### Tia nắng và sương
- Thêm volumetric light/fog theo shadow map của main light, chạy nửa hoặc một phần tư độ phân giải. Ứng viên: [V1] (Render Graph, Unity 6, Forward+/Deferred+; **chưa kiểm license**) hoặc tự viết renderer feature theo cùng cách.
- Giữ haze giải tích của `EnvCommon` cho các dãy núi xa, vì chúng nằm ngoài 50 m bóng.
- Mật độ volumetric gần nhân vật bị giới hạn giống cách fog nhân vật đang bị giới hạn.
- Card `EnvLightShaft` chỉ giữ nếu capture cho thấy còn có ích.

### Cỏ và hoa
- Hệ cỏ riêng trên GPU: compute shader lọc frustum/khoảng cách theo ô, vẽ bằng `Graphics.RenderMeshIndirect`. Đặt theo `GroundHeight` và mặt nạ mật độ (noise theo mảng, đường, hố bom, khu trạm).
- 3–4 loại cỏ + cỏ ba lá + hoa, màu khác nhau từng instance. Gió dùng chung `_EnvWind` (`CombatEnvironment.cs:79`). Cỏ rạp quanh người chơi.
- Trong vùng chơi cỏ thấp, ngoài vùng chơi cao hơn. Cỏ không có collider.
- Không dùng Terrain detail của Unity.

### Cây và đá
- Thay species trong `FloraBiome`/`EnvironmentFlora` bằng thông ba lá + cây non lá rộng, bố cục rừng thưa có khoảng trống.
- Giữ đường LOD/instancing của `FloraScatter` và các luật có test: cây cao cách vùng cover ≥ 5.5 m, không collider.
- Đá scan có rêu và chân hoà vào đất (rêu/đất theo hướng mặt và độ cao so với `GroundHeight`).

### Sky và mây
Mây tích cho preset nắng và tầng mây xám cho preset u ám, làm bằng lớp mây 2D trong `EnvSky` hoặc HDRI CC0 của Poly Haven xoay theo hướng mặt trời. Không dùng volumetric cloud thật (đắt, URP không có sẵn).

### GI và APV
APV chỉ dùng được khi thế giới được dựng lúc edit và lưu vào scene/prefab (cùng các builder, chạy trong Editor). Với hai preset, mỗi preset cần một lighting scenario riêng: phải kiểm tra URP 17.3 có hỗ trợ trước khi làm ([R4]). Đây là bước tuỳ chọn cuối cùng.

### Shader Graph và VFX Graph
- **Shader Graph:** dùng theo từng họ material khi cần lặp art nhanh (cỏ/hoa, đá rêu, nước). Giữ HLSL cho sky, haze, núi và mặt đất. Không port cả tám shader chỉ vì dễ chỉnh. Mỗi keyword/pass làm tăng variant: giới hạn keyword, tách graph theo họ.
- **VFX Graph:** chưa cài. Hợp cho hạt môi trường nhiều mà không ảnh hưởng gameplay (sương thấp, bụi, lá rơi, khói xa) ([G4], [G5]). Giữ ParticleSystem cho máu, chớp nòng, vỏ đạn, cảnh báo: chúng đang được pool/emit trực tiếp từ C#. Không bước nào trong kế hoạch phụ thuộc VFX Graph.

## 5. Địa hình

Mặt đất chỉ dùng để hiển thị, combat không chạy theo nó:
- `CombatEnvironment` không có collider (`CombatEnvironment.cs:8-13`).
- Combat dùng mặt phẳng `GroundY` riêng (`CombatGame.cs:13-15`).
- Test khoá vùng chơi phẳng, flora không collider, bao cát khớp collider cover (`CombatArtMarkerTests.cs:42-90`).

`EnvironmentLayout.GroundHeight` là nguồn độ cao duy nhất cho mesh, flora, không khí, hiệu ứng, máu, shelter và intro (`EnvironmentLayout.cs:142-163`).

**Không chuyển sang TerrainData.**
- `EnvGround` đã hơn URP Terrain Lit: 4 lớp PBR scan trộn theo độ cao, splat, tone map, relief map, SDF đường có vệt bánh, nước đọng và mảnh vụn procedural (`EnvGround.shader:1-6`). Terrain Lit chỉ trộn theo splat hoặc “lớp cao nhất thắng”, không có triplanar ([T1]).
- Vùng chơi phẳng 33×19 m cộng lề 3.5 m (`EnvironmentLayout.cs:14-15`); đồi xung quanh phần lớn được nhìn xuyên qua cây.
- Terrain Tools chỉ cho thêm khả năng sculpt, không đáng một lần refactor lớn.

**Khi cần đổi hình dáng** (đồi tầm trung cho ref1):
- Sculpt trong Blender, xuất heightmap. Gaea / World Machine bị loại.
- `GroundHeight` lấy mẫu bilinear heightmap, giữ nguyên đoạn early-out vùng phẳng (`EnvironmentLayout.cs:145-146`).
- Caller và test vùng phẳng giữ nguyên.
- Texel heightmap phải ≤ `InnerCell` của lưới mesh, nếu không vành hố bom và mực nước trong hố sẽ lệch (`EnvironmentLayout.cs:150-160`).
- Làm spike một ô trước, so capture cùng góc.

Backdrop `EnvironmentBackdrop` (dữ liệu độ cao thật, không collider, không đổ bóng; `EnvironmentBackdrop.cs:8-15`) được phép làm lại: thêm dãy núi nhiều lớp và mây trong thung.

## 6. Công cụ

| Công cụ | Dùng? | Lý do |
|---|---|---|
| Unity Terrain / Terrain Tools / Terrain details | Không | §5; cỏ dùng hệ GPU riêng (§4). |
| Splines | Có | Đường xe và suối (ref3) ([T7]). |
| ProBuilder | Có | Blockout trạm/cover ([T8]). |
| Polybrush | Không | Không còn được hỗ trợ từ Unity 6.3 ([T9]). |
| SpeedTree Modeler | Không | Thương mại; dùng cây scan/asset có LOD sẵn. |
| GPU Resident Drawer | Chỉ khi CPU là nút thắt | Không áp cho `RenderMeshInstanced/Indirect` tự vẽ; có thể tăng VRAM ([H11]). |
| Gaia/GeNa, MicroSplat, Vegetation Studio Pro, Nature Renderer | Không | Trả phí, và `EnvGround` + `FloraScatter` + hệ cỏ riêng đã phủ phần việc. |
| Gaea / World Machine | Không | Người dùng loại. |

## 7. Asset và license

Credit trong README cho mỗi asset: tên, tác giả/người bán, URL, license, ngày tải, file dùng, chỉnh sửa. Với Fab/Asset Store, lưu kèm bản chụp điều khoản và version.

| Nguồn | Tình trạng | Dùng cho |
|---|---|---|
| **Poly Haven** | CC0 ([A5]) | Đá, tảng đá, texture đất/đá, HDRI. |
| **Megascans (Fab)** | Miễn phí chỉ đến hết 2024. Từ 2025 phần lớn trả phí (từ $0.99/asset), có gói starter miễn phí và quà miễn phí hằng tháng; Fab Standard License dùng được cho mọi engine ([A2], [A3], [A6]). Người dùng chưa claim năm 2024 và sẽ mua một số. | Đá rêu hero, vỏ thông, mảng đất. |
| **Unity Terrain Sample Asset Pack** | Asset Store EULA ([A1]) | Texture lớp đất, đá tham khảo. |
| **Book of the Dead** | Demo HDRP ([A4]) | Chỉ tham khảo; shader phải port nếu dùng mesh. |
| **Sketchfab** | Theo license từng model | Xác xe M113/PT-76 cho ref3. |

## 8. Kế hoạch (bước 0–7)

Mỗi bước dùng cùng bốn shot cố định ở 1080p: góc ref1, góc ref2, combat đông, enemy trong cỏ. Bốn shot được chụp lại sau mỗi bước và so với bước trước. Ghi CPU/GPU ms, FPS, free RAM, VRAM. Ngân sách: ≤ 22 ms/frame (~45 FPS) để có khoảng dư trên 40 FPS. Mỗi bước còn có capture grayscale combat và test `CombatArtMarkerTests` phải pass.

| Bước | Việc | Cổng kiểm |
|---|---|---|
| **0 — Baseline** | Chụp bốn shot hiện tại và ghi số liệu. Lưu ở `output/graphics/baseline/` (gitignored). | Bảng số liệu + 4 ảnh. |
| **1 — Nền tảng** | Grade HDR; spike AA (STP + motion vector cho lá/cỏ, hoặc MSAA + AlphaToMask); LOD cross-fade trong `FloraScatter`; ambient từ sky + reflection probe; bóng 4096; refactor `LightingPreset` (hai preset, chọn theo run); sky có lớp mây hoặc HDRI. | Clip chuyển động không nhấp nháy/nhòe; GPU tăng ≤ 2 ms; hai preset đều đọc được combat. |
| **2 — Volumetric light + sương** | Tia nắng có bóng (ref2) và sương dày preset u ám (ref3), giới hạn mật độ gần nhân vật. | ≤ 2 ms GPU ở nửa độ phân giải; enemy nhìn thấy được trong cả hai preset. |
| **3 — Cỏ GPU** | Nhiều loại cỏ + hoa, mặt nạ mảng, màu từng instance, cỏ rạp quanh người chơi; cỏ ở xa do `EnvGround` vẽ. | ≤ 3 ms GPU; enemy ngồi thấp trong cỏ vẫn thấy. |
| **4 — Đổi biome + đá** | Thông ba lá + cây non lá rộng, bố cục rừng thưa; đá scan có rêu và chân hoà đất. | Test flora (không collider, cây cao ≥ 5.5 m khỏi cover) pass; draw/VRAM ghi lại. |
| **5 — Địa hình tầm trung + mây thung** | Spike heightmap sau `GroundHeight` một ô, rồi đồi tầm trung; dãy núi nhiều lớp; mây thung dày hơn (ref1). | Không vết nối hay pop; intro/shelter vẫn đặt đúng; thời gian load ghi lại. |
| **6 — Chi tiết ref3** | Suối theo spline, xác xe, vũng nước phản chiếu trên đường. | Capture ref3 ở preset u ám; không ảnh hưởng combat. |
| **7 — Tuỳ chọn: dựng thế giới lúc edit + APV** | Chỉ làm nếu URP hỗ trợ lighting scenario theo preset. | Bake giữ free RAM ≥ 1 GB; capture đẹp hơn bước 6. |

**Cách đo:** capture trong game dùng PlayMode test tạm chạy `Unity.exe -batchmode -runTests` (có graphics), render camera vào RenderTexture. Batch mode không chạy vòng frame thật, nên số ms ở đó là thời gian render tuần tự (CPU submit + chờ GPU) cho một camera, không phải FPS khi chơi. FPS thật cần Player build; chỉ build khi build không kéo free RAM xuống dưới 1 GB.

## 9. Nguồn

- [H1] Unity 6 render pipelines: https://docs.unity3d.com/6000.3/Documentation/Manual/render-pipelines.html
- [H2] Unity 6 upgrade guides: https://docs.unity3d.com/6000.3/Documentation/Manual/UpgradeGuides.html
- [H3] URP upgrade guides: https://docs.unity3d.com/6000.3/Documentation/Manual/urp/upgrade-guides.html
- [H4] HDRP upgrade guides: https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.3/manual/upgrade-guides.html
- [H5] HDRP Render Pipeline Wizard: https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.3/manual/Render-Pipeline-Wizard.html
- [H6] HDRP material upgrade: https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.3/manual/Material-Upgrade.html
- [H10] Unity texture streaming: https://docs.unity3d.com/6000.3/Documentation/Manual/TextureStreaming.html
- [H11] URP GPU Resident Drawer: https://docs.unity3d.com/6000.3/Documentation/Manual/urp/gpu-resident-drawer.html
- [H12] URP performance optimization: https://docs.unity3d.com/6000.3/Documentation/Manual/urp/optimize-for-better-performance.html
- [R1] URP STP upscaler: https://docs.unity3d.com/Manual/urp/stp/stp-upscaler.html
- [R2] Bật STP: https://docs.unity3d.com/Manual/urp/stp/stp-enable.html
- [R3] URP motion vectors: https://docs.unity3d.com/6000.3/Documentation/Manual/urp/features/motion-vectors.html
- [R4] Adaptive Probe Volumes (URP): https://docs.unity3d.com/6000.3/Documentation/Manual/urp/probevolumes.html
- [V1] Unity URP Volumetric Light (CristianQiu): https://github.com/CristianQiu/Unity-URP-Volumetric-Light
- [G1] Shader Graph 17.3: https://docs.unity3d.com/Packages/com.unity.shadergraph@17.3/manual/index.html
- [G4] Visual Effect Graph: https://docs.unity3d.com/6000.3/Documentation/Manual/VFXGraph.html
- [G5] VFX Graph Getting Started: https://docs.unity3d.com/Packages/com.unity.visualeffectgraph@17.3/manual/GettingStarted.html
- [T1] URP Terrain Lit shader: https://docs.unity3d.com/6000.3/Documentation/Manual/urp/shader-terrain-lit.html
- [T7] Unity Splines 2.8: https://docs.unity3d.com/Packages/com.unity.splines@2.8/manual/index.html
- [T8] Unity ProBuilder 6.0: https://docs.unity3d.com/Packages/com.unity.probuilder@6.0/manual/index.html
- [T9] Polybrush 1.2.1 (không hỗ trợ Unity 6.3+): https://docs.unity3d.com/Packages/com.unity.polybrush@1.2/manual/index.html
- [T10] Gaea Community: https://quadspinner.com/Download
- [T11] World Machine: https://www.world-machine.com/download.php
- [A1] Unity Terrain Sample Asset Pack: https://assetstore.unity.com/packages/3d/environments/landscapes/terrain-sample-asset-pack-145808
- [A2] Megascans miễn phí đến hết 2024: https://www.cgchannel.com/2024/10/epic-games-has-made-megascans-free-to-all-but-only-until-the-end-of-2024/
- [A3] Fab transition FAQ: https://support.fab.com/s/article/Fab-Transition-FAQs
- [A4] Unity Book of the Dead: https://unity.com/demos/book-of-the-dead
- [A5] Poly Haven license: https://polyhaven.com/license
- [A6] Quixel → Fab, giá 2025: https://forums.unrealengine.com/t/upcoming-changes-to-quixel-related-to-fabs-launch-in-october/2024362
