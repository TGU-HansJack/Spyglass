# Spyglass Fork

Vintage Story **1.22.3–1.22.7**，模组 ID：`spyglassfork`，版本：`0.6.7`。
原作者 **Fuami**，分支维护者 **HansJack**。保留原 MIT 许可证及
`Copyright (c) 2021 Fuami`；安装包包含完整 [LICENSE](LICENSE) 与 [NOTICE.txt](NOTICE.txt)。

修复第三人称禁用缩放时仍遮罩/隐藏人物和 HUD、服务端遮罩同步后信息栏未隐藏、
Scribe 任务 HUD 未隐藏、玩家尚未就绪时的空引用及退出后静态状态残留。
服务端配置到达后会重新构建遮罩；零距离插值不会再产生 NaN。

客户端与服务端都需安装。请用本分支替换原版，避免两个相机补丁同时生效。
本分支使用 `assets/spyglassfork`、`spyglassfork.json` 和独立网络通道。
物品代码变为 `spyglassfork:spyglass-*` / `spyglassfork:spytube-*`，旧版物品和配置不会自动迁移。

默认遮罩是方形镜片（`vignetteStyle: "square"`），也可设为 `"none"` 关闭遮罩；圆形轮廓已移除。
方形视野默认边长为原来的 1/2、面积为原来的 1/4（`lensSize: 0.5`），并保持屏幕居中。边长计算为 `(屏幕短边 - edgeSize × 2) × lensSize`，`edgeSize` 默认为 10 像素。金属主体宽度为 21.6 像素（0.6.3 的 3 倍），宽度独立于视野大小；镜片外的眼罩和金属均完全不透光，旧 `edgeOpacity` 字段仅保留兼容。遮罩按选定倍率归一化淡入淡出，避免静止时仍透出场景。按 F4 隐藏界面时仍保留望远镜遮罩。
配置文件位于 `VintagestoryData/ModConfig/spyglassfork.json`。镜片外沿直接复用物品模型的 `game:block/metal/ingot/{metal}` 纹理，采用像素采样与较柔和的暗面，随游戏光照调整明暗，并支持材质包替换。镜片直接使用原版玻璃纹理、颜色和透明度，移除人工随机像素斑点；石英及烟色玻璃保留原版的纹理差异。已有配置可添加 `lensSize: 0.5`，省略时也使用此默认值；开启 `overrideClientConfig` 时，服务端会同步视野大小。

镜片配方仍为竖排「同金属镜筒半件 → 玻璃块 → 同金属镜筒半件」。普通玻璃制作原有望远镜；石英、绿、蓝、紫、红、黄、棕、复古、烟色、粉色玻璃分别制作对应镜片版本，手持模型与观察画面共用该原版玻璃贴图。已有 `spyglass-{metal}` 物品保留，默认普通玻璃；新增版本代码为 `spyglass-{metal}-{glass}`。不同镜片只改变外观、色调与透明度，不增加夜视或缩放奖励。

0.6.6 将水面圆环替换为镜片挂水：雨滴形成水珠，相邻水珠合并，较大的水珠受重力下滑并留下短暂水痕；每颗水珠都有轻微横向漂移，水痕会改变宽度、长度、间距并偶尔断开或分叉，不再沿同一条固定直线。水在金属框内沿底部积聚，再排出。水珠采用低对比度、分级像素边缘和轻微局部折射，避免平滑高光脱离游戏风格。雨停或进入遮蔽处后余水继续下淌、蒸发；水下清除挂水。这是自定义镜片水珠模拟，原版雨量、雨雪类型及屋顶遮挡仍由游戏提供。新雨滴仅在持镜观察且露天降雨时生成，晴天、雪天及冰雹不新增液态雨滴。单次客户端会话保留当前金属/玻璃组合的水，切换到其他组合时重置，不存入物品或同步到服务端。水珠数量有上限；模拟重力按镜片向下，尚未模拟视角倾斜和风对水珠轨迹的影响。

望远镜现在保留游戏本身的大气雾效，并根据实际降水对镜内远景增加轻微的距离衰减：默认满强度降雨在 256 格处额外损失约 15% 对比度，降雪约 25%，冰雹按降雨处理，额外混合量最多 30%。雨雪类型沿用游戏天气与温度判断，不受粒子质量设置影响。太阳附近会出现局部眩光，随游戏太阳遮挡结果、云量和雾密度减弱，默认混合量最多 10%。完全入夜后，新增天气散射不会增亮目标；水下跳过这些效果。

以下设置仅作用于客户端，不随服务端遮罩配置同步；旧配置省略这些字段时使用默认值：

| 配置项 | 默认值 | 作用 |
| --- | --- | --- |
| `enableAtmosphericOptics` | `true` | 环境光学总开关 |
| `weatherOpticsStrength` | `1.0` | 雨雪额外衰减强度，范围 `0–2`，`0` 关闭 |
| `sunGlareStrength` | `1.0` | 太阳局部眩光强度，范围 `0–2`，`0` 关闭 |
| `enableLensRain` | `true` | 镜片水珠、合并、下淌与积水效果 |
| `lensRainStrength` | `1.0` | 镜片雨击强度，范围 `0–2`，`0` 关闭 |

效果随缩放动画淡入淡出，使用 `none` 遮罩时覆盖整个观察画面；禁止第三人称缩放时一同停用。实现使用游戏场景颜色和深度，不会恢复雾中已经丢失的细节，也不新增雨雪粒子。天气取玩家所在地并近似为整条视线的天气；透明物体的距离依赖游戏深度缓冲，云层遮挡仅作近似处理。缺少有效离屏缓冲时跳过效果；着色器失败会暂停效果并记录一次日志，可在游戏中重新加载着色器后重试。

使用 1.22.3 游戏目录打包：

```powershell
./package.ps1 -GamePath E:/MVL/Release/1.22.3
```

每次打包均自动部署：脚本将生成的 ZIP 复制到本机 `C:\Users\29596\AppData\Roaming\MVL\Modpack\测试服务器\Mods`，替换旧的本分支 ZIP，并校验部署文件的 SHA256。可用 `-DeployPath` 指定其他部署目录。更新后重新启动游戏以加载新代码。

兼容性及验证边界见 [分支版验证记录](COMPATIBILITY-FORK.md)。

## Original project

Vintage Story Mod, Adds a spyglass so you can spy on your neighbors from the safety of your windmill!

[![Spyglass prominently displayed upon an anvil.](https://img.youtube.com/vi/oCwqJoa6sP0/maxresdefault.jpg)](https://www.youtube.com/watch?v=oCwqJoa6sP0)

[Discuss the mod on Vintage Story Forums](https://www.vintagestory.at/forums/topic/5240-spyglass-v040/)

[Download via Vintage Story Mod DB](https://mods.vintagestory.at/spyglass)
