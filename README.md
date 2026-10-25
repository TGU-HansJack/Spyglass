# Spyglass Fork

Vintage Story **1.22.3–1.22.7**，模组 ID：`spyglassfork`，版本：`0.6.3`。
原作者 **Fuami**，分支维护者 **HansJack**。保留原 MIT 许可证及
`Copyright (c) 2021 Fuami`；安装包包含完整 [LICENSE](LICENSE) 与 [NOTICE.txt](NOTICE.txt)。

修复第三人称禁用缩放时仍遮罩/隐藏人物和 HUD、服务端遮罩同步后信息栏未隐藏、
Scribe 任务 HUD 未隐藏、玩家尚未就绪时的空引用及退出后静态状态残留。
服务端配置到达后会重新构建遮罩；零距离插值不会再产生 NaN。

客户端与服务端都需安装。请用本分支替换原版，避免两个相机补丁同时生效。
本分支使用 `assets/spyglassfork`、`spyglassfork.json` 和独立网络通道。
物品代码变为 `spyglassfork:spyglass-*` / `spyglassfork:spytube-*`，旧版物品和配置不会自动迁移。

默认遮罩是方形镜片（`vignetteStyle: "square"`），也可设为 `"none"` 关闭遮罩；圆形轮廓已移除。
方形视野默认边长为原来的 1/2、面积为原来的 1/4（`lensSize: 0.5`），并保持屏幕居中。边长计算为 `(屏幕短边 - edgeSize × 2) × lensSize`，`edgeSize` 默认为 10 像素。金属轮廓加粗为之前默认宽度的两倍，宽度独立于视野大小；镜片外的眼罩区域会完全不透光，`edgeOpacity` 只控制金属轮廓，不会让外围黑罩半透明。
配置文件位于 `VintagestoryData/ModConfig/spyglassfork.json`。镜片外沿会按照当前手持望远镜的铜、铁、钢、金等材质变体绘制，并随游戏光照调整明暗。镜片内部已移除额外的玻璃色调、内侧阴影和呼吸式反光。已有配置可添加 `lensSize: 0.5`，省略时也使用此默认值；开启 `overrideClientConfig` 时，服务端会同步视野大小。

使用 1.22.3 游戏目录打包：

```powershell
./package.ps1 -GamePath E:/MVL/Release/1.22.3
```

兼容性及验证边界见 [分支版验证记录](COMPATIBILITY-FORK.md)。

## Original project

Vintage Story Mod, Adds a spyglass so you can spy on your neighbors from the safety of your windmill!

[![Spyglass prominently displayed upon an anvil.](https://img.youtube.com/vi/oCwqJoa6sP0/maxresdefault.jpg)](https://www.youtube.com/watch?v=oCwqJoa6sP0)

[Discuss the mod on Vintage Story Forums](https://www.vintagestory.at/forums/topic/5240-spyglass-v040/)

[Download via Vintage Story Mod DB](https://mods.vintagestory.at/spyglass)
