# Spyglass 0.6.1：1.22.7 与“测试服务器”整合包兼容性审查

> 本文件记录修改前的上游 0.6.1 审查结果。修复后的 Spyglass Fork 0.6.3 验证结果见 [COMPATIBILITY-FORK.md](COMPATIBILITY-FORK.md)。

审查日期：2026-10-25。仓库提交：`6ca2415`。

## 结论

**通过 1.22.7 编译及 Harmony 补丁结构检查；未发现已证实的致命整合包冲突，但存在配置和 HUD 行为缺陷。建议作为候选版本进行整包游戏内验收，暂不能认定为完全兼容。**

当前整合包未安装 Spyglass，已有日志不能证明它在该整合包中运行正常。本次未启动世界进行图形、联网或性能测试，未修改模组实现、整合包配置或存档。仓库仅增加本报告；测试程序、下载和反编译证据保存在仓库外。

## 审查对象及验证

| 项目 | 实际对象与结果 |
| --- | --- |
| 游戏 | `E:\MVL\Release\1.22.7`；整合包 `modpack.json` 与已有客户端日志均确认 1.22.7 |
| 整合包 | `C:\Users\29596\AppData\Roaming\MVL\Modpack\测试服务器\Mods`；读取 120 个模组包/目录的元数据 |
| 代码扫描 | 成功读取 138 个托管程序集；筛出 24 条相关代码线索、60 个相关 JSON 资源后继续人工核对 |
| 非托管 DLL | 5 个 HarfBuzz/rnnoise 原生 DLL 不适用 Cecil 托管分析；不代表这些模组加载失败 |
| 本仓库 | Spyglass 0.6.1，目标框架 .NET 10；以目标游戏程序集编译，0 警告、0 错误 |
| 官方发布包 | [spyglass_0.6.1.zip](https://mods.vintagestory.at/download/93094/spyglass_0.6.1.zip)，同为 .NET 10；相关实现与本仓库一致，资源经换行规范化比较一致 |
| 官方 DLL SHA256 | `04690d62bbd91727a969434ee301dcff4a2469083e18492b7741a613dd630a4c` |
| 发布页标注 | [原模组页面](https://mods.vintagestory.at/spyglass)目前标注 1.22.0-pre.1–1.22.2，并提示 potentially outdated；这本身不能证明 1.22.7 不兼容 |

环境变量 `VINTAGE_STORY` 指向另一处游戏目录，因此本次显式指定上述 1.22.7 路径，避免用错误版本编译。

对本仓库编译产物及官方下载 DLL，均使用游戏实际 Harmony 安装补丁并检查合成 IL，结果如下：

| 补丁目标 | 结果 |
| --- | --- |
| `PlayerCamera.OnBeforeRenderFrame3D` | 成功；相机模式调整调用保留 |
| `ClientMain.Set3DProjection(float,float)` | 成功；FOV 调整调用保留 |
| `SystemRenderEntities.OnRenderOpaque3D` | 成功；普通、批量渲染两处 `Entity.IsRendered` 均替换 |
| `ClientMain.OnMouseMove` | 成功；两处鼠标灵敏度常量均匹配 |
| `GuiDialog.ShouldReceiveRenderEvents` | 成功；默认 HUD 隐藏补丁有效注入 |
| `HudElementBlockAndEntityInfo.ShouldReceiveRenderEvents` | 以 circle 配置启动补丁时成功注入 |

这些检查验证了方法签名、注入位置和补丁合成，未执行实际画面渲染。测试中分别检查 edge、circle 分支，日志中基类 GUI helper 出现两次源于测试重复装载该补丁，不是生产代码默认重复安装。

## 发现的问题

### 1. 中：禁用第三人称缩放后，其他望远镜效果仍然生效

触发条件：`enableZoomInThirdPerson=false`，处于第三人称并使用望远镜。

`EnableEffect()` 仅约束相机切换、FOV 和鼠标灵敏度。HUD 隐藏、ZoomWheel 遮罩和实体隐藏没有检查它，因此会出现不放大画面、却遮罩画面并隐藏 HUD/本机人物的情况，偏离配置注释“只看到人物使用望远镜”的意图；启用滚轮调整时还可能继续消费滚轮事件。

证据：[ClientManipulation.cs](spyglass/src/Client/ClientManipulation.cs) 第 50、89、130 行，[ZoomWheel.cs](spyglass/src/Client/ZoomWheel.cs) 第 19 行，以及 [ImmersiveFpSystemRenderEntities.cs](spyglass/src/Client/Patches/ImmersiveFpSystemRenderEntities.cs) 第 18 行。

使用实际 DLL，在模拟“本机玩家、已完全缩放”状态下得到：

```text
FovMultiplier=1 HideGuis=True OverlayEnabled=True LocalPlayerRendered=False
```

这是隔离逻辑验证，未模拟游戏完整输入流程。默认配置为 `true`，默认使用不触发此问题。建议统一以“允许并实际生效的缩放状态”控制上述副作用；修复前保留默认值并使用第一人称。

### 2. 中：服务端同步圆形遮罩后，方块/实体信息栏的隐藏补丁缺失

触发条件：客户端以默认 `vignetteStyle="edge"` 启动；服务器配置 `overrideClientConfig=true`，并下发 `circle`、`square` 或 `box`。

客户端先在 `SpyglassMod.StartClientSide()` 创建 `ClientManipulation` 并安装补丁，随后注册接收服务器配置。`GuiDialogHudPatch.patch()` 仅在安装当时为非 edge 时补丁化信息栏；网络回调只修改配置字段，不重装补丁。

实际 Harmony 验证：edge 启动后模拟同样的字段同步为 circle，`block HUD patched=False`。因此会保留 edge 模式下的信息栏显示行为，与本地直接以 circle 启动的行为不一致。

定位：[SpyglassMod.cs](spyglass/src/SpyglassMod.cs) 的 `StartClientSide`、[GuiDialogHudPatch.cs](spyglass/src/Client/Patches/GuiDialogHudPatch.cs) 的 `patch`。

建议始终安装信息栏补丁，再于运行时按当前配置判断是否隐藏。临时做法是客户端启动前也配置相同的非 edge 样式。0.6.0 已加入服务端配置同步，但该启动顺序问题仍存在。服务器默认 `overrideClientConfig=false`；仅修改服务器遮罩配置不会强制客户端采用。

### 3. 低：Scribe 任务 HUD 不会按 Spyglass 的通用规则隐藏

整合包有 Scribe 1.4.3、Gui 3.1.0。`Scribe.HudScribePins.ShouldReceiveRenderEvents()` 自行返回 `IsOpened()`，没有调用 Spyglass 补丁化的 `GuiDialog` 基类方法。1.22.7 GUI 调度器使用虚调用，GuiBase 的绘制路径也没有调用该基类判断，因此开启任务追踪时会绕过 Spyglass 的 HUD 隐藏规则。最终遮挡关系仍需游戏内观察。

这是实现可确认的显示兼容缺口，不是已证实的崩溃。若希望隐藏任务面板，应增加针对该重写方法的可选兼容补丁，或通过 Scribe 自身功能关闭任务追踪显示。

### 4. 防御性改进：本机玩家判定缺少空值保护

[ClientManipulation.cs](spyglass/src/Client/ClientManipulation.cs) 的 `IsLocalPlayer()` 直接访问 `capi.World.Player.Entity`，实体渲染 helper 对每个玩家实体都会调用它。如果渲染或物品回调发生在本机玩家尚未就绪/已经清理的阶段，可能空引用。尚未复现 1.22.7 实际生命周期下的崩溃，不能将它认定为评论崩溃的根因。

`AttemptingToZoom()` 已有逐级空值检查，不能把评论中 0.5.0 的同名方法崩溃直接套到当前版本。建议对 `IsLocalPlayer` 同样保护，并在退出时明确清理缩放及相机恢复状态；静态状态当前没有显式复位，需要退出/重进世界测试确认。

## 整合包交叉检查

| 模组 | 发现与判断 |
| --- | --- |
| Walkable Entities 1.0.33 | 与 Spyglass 修改同一个相机方法。用实际 DLL 按两种安装顺序联合安装均成功，`AdjustCameraMode` 与 `Align` 都保留；仍需在移动船只/载具上检查相机方向、抖动和恢复 |
| Body Physiology 2.16.8 | 肾上腺素影响 FOV。其 getter 补丁找不到 1.22.7 的 FOV 属性，因为游戏提供字段；已有日志也记录回退到整数图形 FOV 设置。没有发现与 Spyglass 投影乘数补丁直接覆盖的证据，仍需测试肾上腺素开始/结束期间的放大倍率和 FOV 恢复。这条回退警告在未安装 Spyglass 时已经存在 |
| Komet 2.0.0 | 实体细分优化修改 `SystemRenderEntities.OnBeforeRender` 与 `EntityShapeRenderer.BeforeRender`，没有直接修改 Spyglass 的 `OnRenderOpaque3D`；未发现直接补丁冲突，不能据此保证复杂场景的帧率 |
| Scribe 1.4.3 / Gui 3.1.0 | 任务 HUD 绕过基类隐藏补丁，见问题 3 |
| Toolsmith 1.2.19 | 配置中明确识别 spyglass，分别纳入 bluntheadedtools、singleparttools 正则；未发现由这些配置导致的直接冲突。拆装、配方及地面存放仍应游戏内验收 |
| Carry On、Crawl & Dive、Dead、Downed、Shipwright、Immersive Maps 等 | 筛中的玩家实体/模型资源共 136 个补丁操作，均为 add/addmerge；未发现替换/删除整个动画集合的操作或另一个 `spyglassing` 定义。不能排除同时运行姿势动画时的叠加问题 |
| VSCN server mod fix 1.2.81 | 检索命中鼠标事件相关 UI/诊断文本；未建立它与 `ClientMain.OnMouseMove` 的直接补丁冲突 |
| Spyglass Info Range、独立 Zoom、Toolbelt/Quivers | 本次模组清单未检出，评论中针对这些模组的情况不能直接归于当前整合包 |

1.22.7 原版玩家动画集合、模型动画集合及 `UpperArmR` / `LowerArmR` 均存在，Spyglass 的追加路径仍有效。原版 `survival/blocktypes/glass/full-plain.json` 仍生成 `game:glass-plain`，金属锭变体仍来自原版金属属性；未发现配方关键代码因 1.22.7 更名而失效。这里只检查资源定义，没有完成游戏资源合并、配方注册和实际锻造测试。

## 原模组评论区核对

来源：[Spyglass 发布页及评论](https://mods.vintagestory.at/spyglass)。无年份的日期保留页面原显示。

| 评论 | 审查结论 |
| --- | --- |
| basfrasse，Jul 8：卡顿/冻结，停用后恢复 | 有价值的性能线索，但未提供明确版本、模组清单、日志或复现条件。本次没有性能实测，不能确认或否定。特别核查了遮罩界面反复重建：1.22.7 的 `GuiComposerManager.Create` 会 Dispose 同名旧 composer，不能据此指控显存泄漏 |
| Hideo，May 19：1.21.6，`EntityShapeRenderer.DoRender3DOpaqueBatched` 空引用，Harmony IDs 含 spyglass | 是旧版渲染风险线索；Harmony ID 说明方法被修改过，不能单凭它确定根因。当前 1.22.7 注入测试通过，但这也不等于已证明渲染路径绝无崩溃 |
| Tirello，May 15：1.22.2 无错误；SnakeMittens，Apr 24：1.22.0 可用 | 支持同代版本运行的历史证据，不代替 1.22.7 验证 |
| zartax35，Feb 4：1.21.0 隐藏 HUD 但不放大，怀疑 Zoom 冲突 | 未有确证；本包未检出独立 Zoom。当前另有“禁用第三人称缩放”的可解释触发条件，见问题 1，不能声称就是该评论的原因 |
| RinVindor，2025-09-04：组合左右键后缩放卡住、远距离交互；作者 09-06/09-13 回复 | 作者称 1.21.1-rc.3 行为已有改变，且远距离交互涉及 Spyglass Info Range/创造模式。当前 Spyglass 只改渲染 FOV，没有改交互距离；本包未检出 Info Range。仍保留组合输入回归测试 |
| Kawsiphek，2025-09-06：服务端遮罩设置无效 | 当前已有 `overrideClientConfig` 开关，默认关闭，只同步样式、边缘透明度、边缘尺寸。同步顺序仍存在问题 2 |
| XurxoMF，2024-02-25：0.5.0 / 1.19.3 入服空引用 | 当时建议更新到 0.5.1；当前 `AttemptingToZoom` 已有空值保护，不应当作当前版本必现问题 |

老版本关于副手、腰带和 AI 的讨论，没有提供当前 1.22.7 的故障证据。当前缩放判定只识别激活快捷栏中的 Spyglass，不能预期支持副手使用；服务端代码没有修改生物 AI。

## 安装条件与剩余验收

`modinfo.json` 声明 Universal、`RequiredOnClient=true`、`RequiredOnServer=true`。若加入服务器整合包，需要客户端与服务端都安装同一兼容版本；它包含物品和配方，不能作为纯客户端缩放工具处理。`game:1.22.0` 是最低依赖声明，不是“仅允许 1.22.0”。

建议在测试世界完成以下验收后再确定长期使用：

1. 第一人称、普通第三人称、沉浸视角各测试使用、松开、切槽和相机恢复；重复开关至少 100 次观察帧时间和显存趋势。
2. 按住右键时按住左键，再先松右键；切槽、蹲下、打开菜单、失去焦点后确认缩放能退出，生存模式交互距离正常。
3. 在 Shipwright / Walkable Entities 的移动船只或载具上缩放；同时触发 Body Physiology 肾上腺素；效果结束后检查 FOV 与方向恢复。
4. 分别以 edge/circle 启动，测试服务器强制遮罩与非强制模式；开启 Scribe 追踪、地图和生理 HUD，检查遮挡关系。
5. 退出重进世界、服务器重连、其他玩家加入/离开，以及实体多的场景检查日志；关注 `spyglass`、`EntityShapeRenderer` 和 `NullReferenceException`。
6. 手册查配方、锻造铜管、合成望远镜，测试 Toolsmith 相关处理、地面存放以及其他玩家看到的使用动画。

## 可复核证据

证据目录：`E:\vintagestory\Spyglass-compat-audit-20261025`。

- `final-results.txt`：官方 DLL 的补丁安装、两种 Walkable 顺序、配置顺序及第三人称逻辑探针结果。
- `combined-results.txt`：本仓库编译产物的联合补丁结果。
- `inventory.json`、`code-hits.json`、`asset-patches.json`、`asset-results.txt`：清单、筛选线索、资源操作及检查结果。
- `spyglass-page.html` / `.txt`、`upstream-0.6.1.zip`：原发布页快照及下载包。
- `Probe/Program.cs`：检查程序；`game/`、`mods-src/`：相关反编译证据。

可复跑的主要命令：

```powershell
dotnet build spyglass\spyglass.csproj -c Release -p:VINTAGE_STORY=E:/MVL/Release/1.22.7 --nologo
dotnet run --project E:\vintagestory\Spyglass-compat-audit-20261025\Probe\Probe.csproj -c Release -- --patch-only --combined --behavior --upstream
```

探针为隔离进程，不调用游戏世界启动，也不连接服务器。程序集/资源筛选使用方法名、类型名及资源路径等静态线索，无法穷尽反射、动态补丁和运行期交互；性能评论尤其需要实际图形环境验证。
