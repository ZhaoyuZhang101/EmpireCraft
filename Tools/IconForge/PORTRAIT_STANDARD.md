# 终端立绘与衣冠

立绘使用正脸和正面站姿，以约 6.2 头身为基准。男版双臂自然下垂、双脚略分，女版双手叠在腰前、双脚靠拢。服装绘制时按同一底身对齐，帽子和头发独立绘制。

## 画布

- 每格 88×136 像素，中轴是 x=44。颅顶约 y=14，衣领在 y=37 左右，脚底约 y=134。发髻、冠顶留在上方预留区。
- 画完整的衣身、手和鞋。衣领上方留透明，帽子和发式留出脸部开口。添加一件男女通用的服饰时，分别画两种站姿，保持同一颈口和脚底线。
- 固定光线从左上来。沿用深色描边、成块的像素明暗、衣褶和纹样。

## 图层与图集

图层顺序为后发、底身、脸、衣着、面纹、胡须、前发、冠帽。完整服饰已包含姿势所需的手和鞋，底身在缺少服饰时显示。衣领叠在脖子前。长发分前后层，冠帽可在 `HAT_CLIP` 指定压住帽檐以上的前发，或用 `all` 同时收起前后发。

`portrait_art.png` 每行十格，`portrait_art.json` 的序号从左上角的零开始。图格名示例为 `body:ming:m`、`body:ming:f`、`hat:yishan`、`hair:taregami` 和 `back:taregami`。Alpha 0 为空白，255 为普通像素，254 标记可换肤色的脸、颈和手，253 标记瞳孔。页面合成时保留原画的明暗，并将这些像素换成角色的肤色、瞳色。

## 加入衣冠

1. 将服饰画在同一尺寸的透明格，按性别分别对齐。在源图集追加格子，并在 `portrait_art.json` 登记序号。
2. 在 `portrait_forge.py` 登记稳定的装备 ID、名称、文化、性别、类别、时期和默认冠帽。已有装备的 ID 用于终端衣柜。
3. 运行下列命令重新打包，然后在终端衣冠页检查完整立绘和小头像。

   ```sh
   python Tools/IconForge/portrait_forge.py
   python Tools/IconForge/terminal_portraits.py
   ```

打包会生成 `Terminal/portraits.png`、`portrait_parts.json` 和供服务端计价的 `Terminal/wardrobe.json`，并把 WebP 图集嵌入 `Terminal/index.html`。打包脚本会核对 Alpha 标记并检查页面是否符合终端中继的大小上限。
