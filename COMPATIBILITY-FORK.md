# Spyglass Fork 0.6.3 兼容性验证

验证日期：2026-10-25。模组 ID：`spyglassfork`。作者：Fuami、HansJack。
基于 Spyglass 0.6.1，原项目：https://github.com/fuami/Spyglass 。
原发布页及评论：https://mods.vintagestory.at/spyglass 。

## 本次修复

- 禁止第三人称缩放时，遮罩、HUD 隐藏、人物隐藏、鼠标灵敏度调整和滚轮拦截一同停用；首次第三人称帧也使用实际相机模式判断。
- 方块/实体信息栏补丁始终安装，按当前配置决定显示；服务端同步后请求重新构建遮罩。遮罩现在只提供 `square` 和 `none` 两种模式，移除圆形轮廓。
- 方形镜片外沿按手持望远镜的金属变体绘制多层倒角、高光和暗面，并根据玩家所在位置的 RGB/太阳光照调整材质亮度。
- 加入很小的呼吸式光学亮度反馈，不使用全局镜头抖动，减少眩晕和与其他相机模组的冲突。
- 为 Scribe 的独立 HUD 渲染入口添加可选兼容补丁；未安装 Scribe 时无需该依赖。
- 玩家/API 尚未就绪时安全返回；退出时移除 Harmony 补丁、注销计时监听、释放遮罩并清理客户端引用和缩放状态。
- 零距离插值直接返回目标值，避免同一计时点产生 NaN。
- 使用独立的资源域、物品类、动画代码、配置文件、GUI 名称、Harmony ID 和网络通道。

## 版本验证

以 **1.22.3** 游戏程序集编译一个 .NET 10 DLL，再将同一 DLL 加载到以下各版真实游戏程序集环境。使用游戏附带 Harmony 安装补丁、检查生成 IL，并进行无图形界面的行为回归。

| 游戏版本 | 程序集来源 | 回归结果 |
| --- | --- | --- |
| 1.22.3 | 本机 Windows 游戏安装 | 53 项通过 |
| 1.22.4 | 官方 Linux 服务端发行包中的托管程序集 | 53 项通过 |
| 1.22.5 | 本机 Windows 游戏安装 | 53 项通过 |
| 1.22.6 | 本机 Windows 游戏安装 | 53 项通过 |
| 1.22.7 | 本机 Windows 游戏安装 | 53 项通过 |

1.22.4 来源：https://cdn.vintagestory.at/gamefiles/stable/vs_server_linux-x64_1.22.4.tar.gz 。其 VintagestoryLib 含测试所需客户端类型；此项验证不等于运行 Linux 图形客户端。

检查包括相机/FOV/人物渲染/鼠标/HUD 补丁注入、首次第三人称判断及恢复原相机模式、第三人称效果停用、遮罩配置变化、实际 Scribe HUD 方法、Walkable Entities 相机补丁的两种安装顺序、空引用防护、插值与客户端状态清理。安装包中的 DLL 单独取出后重复相同版本矩阵验证。

## “测试服务器”整合包

审查目录：`C:\Users\29596\AppData\Roaming\MVL\Modpack\测试服务器\Mods`。
原始审查扫描 120 个模组、138 个托管 DLL，并检查游戏资源补丁；未在整合包中安装或替换文件。

- **Scribe**：加载整合包实际 DLL，验证缩放时隐藏任务 HUD、关闭隐藏选项及结束缩放后恢复原渲染行为。
- **Walkable Entities 1.0.33**：加载实际 DLL，两种补丁安装顺序下，相机 IL 同时保留两方注入。
- **BodyPhys 2.16.8**：整合包中原有相机属性 getter 补丁失败在不加载 Spyglass 时也存在，属于其自身与游戏版本适配问题；未证实由本模组造成。
- **Komet**：涉及的渲染回调不同，静态审查未发现相同补丁目标冲突。

没有启动完整整合包世界进行图形渲染、长时间性能或多人联机测试。原评论区的旧版渲染崩溃及未明确复现步骤的卡顿报告未复现，不能据此宣称全部解决。以上结果证明所测程序集及回归行为通过，不保证所有模组组合或所有游戏场景均无问题。

## 安装与版权

客户端与服务端均安装此 ZIP；使用本分支替换原 Spyglass，避免同时启用两套相机补丁。
游戏依赖最低版本为 `1.22.3`；本次验证范围仅为 `1.22.3–1.22.7`。

资源域已改为 `spyglassfork`，物品代码为 `spyglassfork:spyglass-*` 和 `spyglassfork:spytube-*`，配置文件为 `spyglassfork.json`。旧物品和 `spyglass.json` 不会自动迁移。既有世界替换前应备份并安排旧物品迁移。

原始 MIT LICENSE 原样保留，版权声明仍为 **Copyright (c) 2021 Fuami**。包内包含 LICENSE、NOTICE.txt、README.md 和本验证记录；HansJack 已加入模组作者列表和程序集作者元数据。

## 复验

源码附带 `tests/CompatibilityTests`，不需要测试框架 NuGet 包。

```powershell
dotnet build tests/CompatibilityTests/CompatibilityTests.csproj -c Release -p:VINTAGE_STORY=E:/MVL/Release/1.22.3
dotnet tests/CompatibilityTests/bin/Release/net10.0/CompatibilityTests.dll <游戏目录> <spyglassfork.dll> <可选模组DLL目录>
```

最后一个目录应包含审查时的 Scribe.dll、Scribe.Core.dll、Gui.dll、WalkableEntities.dll 及它们所需的其他依赖。省略该参数则跳过 Scribe/Walkable 联合检查，断言数量相应减少。第三方模组 DLL 不随本安装包再分发。

打包命令：`./package.ps1 -GamePath E:/MVL/Release/1.22.3`。显式传入目标游戏目录，避免环境变量指向其他安装。
