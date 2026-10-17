# 小松鼠图标

使用内置 image_gen 工具生成透明 PNG，随后通过 Windows 自带 System.Drawing 缩放并封装为 ICO，无新增第三方依赖。

- 当前原图：`squirrel-dynamic.png`（双炭灰色、向前跃起的松鼠）
- 当前 Windows 图标：`QuickLauncher-dynamic.ico`
- 本轮配色为 #3C3F41 与 #2B2B2B，以姿态表现动感；图标本身是静态图标。
- 初版原图 `squirrel.png` 和图标 `QuickLauncher.ico` 保留供参考。
- ICO 尺寸：16、20、24、32、40、48、64、128、256 像素，32 位透明通道。
- 重建 ICO：在项目根目录执行 `powershell -NoProfile -ExecutionPolicy Bypass -File .\build-icon.ps1`。
- 程序编译时将 ICO 同时嵌入 EXE 图标和窗口使用的程序集资源，不依赖旁边的图片文件。

## 当前动感版提示词

使用内置 image_gen 工具，以第一张 2D 松鼠为编辑对象，改变配色和姿态。

```text
Use case: style-transfer. Edit target: the provided QuickLauncher squirrel icon. Redesign this mascot as a bold contemporary TWO-COLOR flat logo with a much more dynamic pose: a small squirrel leaping diagonally forward to the right at full speed, body leaning forward, one paw reaching ahead, hind feet swept back, huge flowing tail sweeping behind to the left into a single powerful curved swoosh. Compact sporty delivery-mascot energy, cheerful and agile, own original squirrel design. Simplify the acorn into a small lightning-shaped cutout near the chest; prioritise the active silhouette over props. STRICT palette: only solid charcoal gray #3C3F41 and deeper charcoal #2B2B2B; outside and intentional negative-space cutouts must be truly transparent. No orange, no yellow, no cream, no white fill, no extra colors. Use transparent negative spaces to separate limbs, tail and eye so the shape reads at 32px even with the restrained charcoal palette. Very few broad smooth geometric shapes, crisp vector-like edges, bold clear silhouette, simplified face, no fur strokes, no gradients, no shadows, no texture, no 3D, no background tile, no typography or text, no motion blur. Square transparent icon with comfortable margin around complete mascot; one finished symbol only.
```

## 之前的 2D 版提示词

使用内置 image_gen 编辑初版图标。用户选择第一张 2D 图（眼睛带白点），当前使用第一轮编辑结果。第二轮结果 squirrel-flat.png 和 QuickLauncher-flat.ico 保留供参考。完整提示词如下。

```text
Use case: style-transfer. Edit target: provided squirrel app icon. Redesign it into a contemporary minimal 2D flat app icon, not a vintage cartoon. Preserve concept only: friendly orange squirrel, large curled tail, holding acorn with lightning bolt, transparent background. Radically simplify silhouette into a few bold smooth geometric shapes. Strictly solid flat fills, 4 colors total: warm orange #F47732, cream #FFF2D8, dark navy #25324A, muted golden yellow #F9BE48. Tail is one clean rounded orange spiral shape, no tufts, no layered fur. Small black dot eyes, tiny simple nose, no glossy anime eyes, no open mouth. Acorn cap is a single solid navy shape without scales, acorn body golden with a single simple cream lightning bolt. No gradients anywhere, no shading, no gloss, no highlights, no shadows, no textures, no strokes describing fur, no 3D volume, no decorative tiny details. Contemporary clean vector-style logo appearance, cute compact balanced silhouette with sufficient breathing room at edges, optimized for 16px and 32px Windows icon sizes. Single centered icon, square image, preserve real alpha transparency outside silhouette, no background tile, no text.
```

第二轮编辑（未采用）：

```text
Edit target: the supplied simplified squirrel icon. Keep the exact existing geometry, silhouette, pose and composition. Change ONLY surface rendering: make every colored region a single absolutely uniform solid color. Orange regions all uniformly #F47732; cream regions uniformly #FFF2D8; navy regions uniformly #25324A; yellow acorn uniformly #F9BE48. Remove ALL gradients, lighting variations, textures, grain, noise, smudges, edge glow and shadows. Absolutely flat vector-style filled paths, crisp clean antialiased edges. Remove white eye highlights, use simple uniform dark eyes. Keep the tail spiral, acorn and lightning bolt. True transparent background, no solid rectangle, no checkerboard. The result must look like a minimal 2D SVG pictogram rendered to PNG, NOT painted, NOT shaded. No text. No new elements.
```

## 初版生成提示词

```text
Use case: logo-brand. Asset type: production Windows desktop app icon for QuickLauncher. Generate one square icon, a charming compact orange-brown squirrel mascot hugging one golden acorn with a bold cream lightning bolt on its front. Huge curved fluffy tail wraps behind the squirrel into a near circular silhouette. Small friendly face, two clean dark eyes, cream muzzle and chest, rounded ears. Flat cartoon vector-like illustration with large clean shapes, warm burnt orange and amber, deep warm brown outlines, very restrained highlights, premium friendly utility app personality. Centered composition fills about 88 percent of square; genuinely transparent background outside the squirrel silhouette. Must be recognizable down to 32 pixels: minimal details, bold silhouette, strong contrast, no tiny fur strokes, no scene, no enclosing app tile, no letters, no text, no mockup, no watermark. Single finished icon only.
```
