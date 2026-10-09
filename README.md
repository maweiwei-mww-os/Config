# Game Config

最新版本 **0.4.0**：[下载 Unity UPM Package](Distributions/com.game.json-0.4.0.tgz) · [中文使用指南](Packages/com.game.json/使用指南.zh-CN.md) · [验证报告](Packages/com.game.json/VERIFICATION.md)。

本仓库包含独立配置框架、构建工具、测试和作为消费者的骰子小镇源码。Unity 缓存、游戏可执行文件、发行版游戏 ZIP 和含本机授权标识的原始 Unity 构建日志不上传；下文提及的游戏 EXE 需在本机构建。结构化验证报告和测试结果保留于 `Tests/Evidence`。

## 示例游戏：骰子小镇 / Dice Town

一个离线、单机、可独立运行的 Unity 棋盘小游戏，同时用真实游戏配置对比普通 JSON 对象存储和 Game-Json 紧凑存储。

## 框架最新交付

**框架本身已升级到 0.4.0**：通用类型化构建模型、JSON/CSV/自定义来源、独立运行时程序集。
安装和扩展接入见 [中文使用指南](Packages/com.game.json/使用指南.zh-CN.md)，精确验收范围见 [验证报告](Packages/com.game.json/VERIFICATION.md)。
独立包：`Distributions/com.game.json-0.4.0.tgz`。实际 Windows IL2CPP 验证、强类型对象基线、
三种数据规模和原始结果见 `Tests/Evidence/0.4.0`。执行 `scripts/run-framework-benchmark.ps1`
可直接运行已编译验证程序并输出性能表，不需要启动 Unity。

下文小游戏及其旧性能面板作为消费者保留，不作为新版框架性能数据。

## 直接玩

打开 `Builds/Windows/DiceTown.exe`。分发时请保留同目录的 Data、MonoBleedingEdge 和 DLL 文件，不能只拿走 exe。
完整游戏压缩包：`Distributions/DiceTown-Windows.zip`。新版独立配置包：`Distributions/com.game.json-0.4.0.tgz`。
框架改动与独立验证见 `Packages/com.game.json/README.md`；已有小游戏发行包的性能截图属于先前版本，不作为新版框架的性能结果。

要现场查看完整性能指标，打开 **`Builds/Performance/DiceTown.exe`**，点击右下角 **「性能实测」**。
这是同一个可玩游戏的 Development 验证版，包含 Unity 分配采样标记；完整分发包为 `Distributions/DiceTown-Performance.zip`。
`Builds/Windows` 为 Release 版，本机该版本不能使用 GC.Alloc 分配标记，因此零分配项明确显示「未验证」。两个版本的耗时不可混为同一组数据。

- 掷骰走格，获得金币、补给和护盾；车站收益更高，事件格可能扣钱，护盾可抵挡。
- 用金币升级三座建筑。每升一级，后续正收益增加 5 金币。
- 三座建筑都达到 5 级后前往下一镇。骰子耗尽可以领取补给，不需要联网或付费。
- 左下角切换配置后端或日常/丰收节。切换不会重置进度；已经开始的一次掷骰保持原奖励。
- 右下角「性能实测」运行现场测量，也可故意加载损坏数据，验证旧配置不受影响。
- 自动保存到 `%USERPROFILE%/AppData/LocalLow/GameJson/Dice Town/town-save.json`。重新开始会弹出确认。

游戏包含 120 镇的参数化配置，共 7,560 条记录；目前复用同一套棋盘美术，不是 120 套独立场景。美术由原创程序化 3D 几何组成，没有使用 MONOPOLY 的商标或素材。

## Unity 工程

使用 Unity 2022.3 打开此目录，打开 `Assets/Scenes/Town.unity` 并 Play。
本机验证版本是 2022.3.60f1c1。菜单 `Town/Prepare game` 可重建场景和紧凑配置；已有 JSON 源数据不会被该菜单覆盖。
配置源位于 `Assets/Resources/Config/town_v1.json` 和 `town_v2.json`。修改后执行 Prepare 或选中 JSON 后通过 Tools/Game-Json 编译。
不需要安装 .NET SDK 即可玩游戏或在 Unity 中编译配置；核心测试命令需要 .NET 8 SDK 或更高版本。

## 独立配置包

`Packages/com.game.json` 是可独立安装的 UPM 包，包含运行时、JSON 校验与编译、Editor 菜单以及 Quick Start 示例。
包不会依赖这个小游戏。安装说明和 API 位于包内 README。
这是一套新的、受原版列存储思想启发的实现，没有修改原始 Game-Json 或已有 GameDataPlatform 工程。

## 对比边界

面板顶部直接显示混合读取吞吐倍率、配置常驻内存节省比例、20 万次读取新增内存。下面分为三个页签：

| 页签 | 现场测量 |
| --- | --- |
| 访问速度 | ID 定位、按 ID 读数值、读字符串长度、遍历列表、混合读取；各项 20 万次的耗时与倍率，混合读取平均 ns/次 |
| 内存与加载 | 配置常驻估计、均摊每条字节、输入文件体积、加载与校验耗时、加载分配次数 |
| 零分配验证 | 读取分配事件次数、新增内存、测量期间进程 GC 回收次数、主动创建 4 KB 数组的阳性校准、空操作校准 |

- 两种后端驱动同一个游戏、读取相同配置、使用相同查询序列并检查校验和。
- Object JSON 是每行字典、共享字段名、预建 ID 索引的常规基线，不是原版 Game-Json 的 CompactConfig。
- 7,560 行全部参与加载与内存对比；混合查询覆盖其中 2,880 条奖励，读金币、字符串和列表。
- 每项 20 万次查询预热后测量 5 轮，报告中位数。所有随机 ID 均命中现有奖励表，不代表稀疏 ID、缺失查询或全部配置规模。速度倍率不等于游戏 FPS 提升。
- 加载耗时为一次观测，包含解析/校验，不包含磁盘 I/O；不是统计置信区间。分配采样是单独一遍，不计入测速结果。
- 常驻内存是完整 GC 前后托管堆差值的近似，不是进程总内存、显存或精确 Memory Profiler 对象统计。
- Unity 使用 `ProfilerRecorder` 记录主线程同步调用范围内的 `GC.Alloc` 事件，先验证主动创建数组至少触发一次事件、空操作为零次。检测失败显示「未验证」。这不是线程安全测试。
- `GC.Alloc` 的 `ProfilerRecorderSample.Value` 单位是耗时，不能作为字节数。只有校准通过且事件数为零时，才由此推断新增托管内存为 0 B；非零事件仅报告次数，不伪造字节数。
- .NET 测试使用经阳性校准的 `GC.GetAllocatedBytesForCurrentThread`，与 Unity 证据分开。两种后端预热后的读取都可能零分配；零次 GC 回收不等于零分配。
- JSON 输入、二进制输入和游戏其他对象不计入两种配置图的常驻差值。转换成本移到了构建期，并未凭空消失。
- 配置加载会分配；游戏动画、UI 和存档也会分配。没有宣称整个游戏零 GC。

## 验证

```powershell
dotnet run --project Tests/CoreTests.csproj -c Release -- --evidence
powershell -ExecutionPolicy Bypass -File scripts/build.ps1
powershell -ExecutionPolicy Bypass -File scripts/build.ps1 -Performance
```

Unity Player 的 `--smoke-output <目录>` 参数执行独立测试，不读取或覆盖玩家存档。输出棋盘、升级后状态、对比和拒绝损坏配置的截图，及 `comparison.json`、`smoke-result.json`。
本地 .NET 数字与 Unity Player 数字分别存放，不混用。`Artifacts` 中的日志记录当前验证结果。
每次现场测量保存 `comparison.json` 到游戏存档目录；自动验证时保存到指定输出目录。报告包含 Unity 构建类型、CPU、各项原始数值和校准结果。

采样 API 参考：[Unity 官方 GC.Alloc 事件计数示例](https://docs.unity.cn/2022.3/Documentation/ScriptReference/Unity.Profiling.ProfilerRecorderOptions.SumAllSamplesInFrame.html)。

## 当前范围

已实现：单机闭环、存档恢复、配置后端切换、活动整组切换、紧凑列存储、字符串共享、只读列表、跨表校验、校验和、独立 UPM 包和测试。
框架 0.2.0 已增加强类型 C# 生成、引用契约校验与并发安全的快照发布，保留原动态 API。
尚未承诺：通用嵌套 JSON、远程补丁下载/签名、Lua 桥接、多文件崩溃恢复及移动端性能。具体运行平台以本轮框架验证记录为准。
当前版本是实际可玩、可验证的交付起点，不把尚未实现的能力算进“9 分”。
