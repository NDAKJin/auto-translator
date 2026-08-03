### 路径与参考资源

- RimWorld 安装路径：`C:\Application\steam\steamapps\common\RimWorld`
- RimWorld 本地模组存放路径：`C:\Application\steam\steamapps\common\RimWorld\Mods`
- RimWorld 核心 DLL 路径：`C:\Application\steam\steamapps\common\RimWorld\RimWorldWin64_Data\Managed\Assembly-CSharp.dll`
- RimWorld UnityEngine DLL 路径：`C:\Application\steam\steamapps\common\RimWorld\RimWorldWin64_Data\Managed\UnityEngine.dll`
- RimWorld 官方 DLC 扩展目录：`C:\Application\steam\steamapps\common\RimWorld\Data`
- RimWorld 其他 Mod（Steam 创意工坊）安装目录：`C:\Application\steam\steamapps\workshop\content\294100`
- Harmony 模组路径：`C:\Application\steam\steamapps\workshop\content\294100\2009463077`
- RimWorld 官方 DLL 反编译路径：`C:\Users\nomad\Documents\Workspace\rimworld\RimWorldDecompiled`
- RimWorld Mod 教程 / 维基翻译文档：`C:\Users\nomad\Documents\Workspace\rimworld\PatchTranslation\notes\mod教程`
- RimWorld 中文维基 Mod 开发教程：<https://rimworld.huijiwiki.com/wiki/MOD%E6%95%99%E7%A8%8B>
- RimWorld 英文维基 Mod 开发教程：<https://rimworldwiki.com/wiki/Modding_Tutorials>
- 临时反编译代码存放目录：`C:\Users\nomad\Documents\Workspace\rimworld\AutoTranslator\data\decompiled`

### 仓库地址

- Git 仓库（Gitee）：<https://gitee.com/nomadicooer/auto-translator.git>
- 默认分支：`main`（远程 `origin/main`，已设上游跟踪）
- `.gitignore` 约定（不进仓库）：`Source/**/bin/`、`Source/**/obj/`、`Publish/AutoTranslator/1.6/Assemblies/`（构建 DLL）、`data/`、`Extends/`（符号链接，换机需重建）、`.codebuddy/`（记忆目录）、`.vs/`

### 开发环境

- 操作系统：Windows 64 位
- .NET SDK：9.0.316 / 10.0.302（用于编译 C# 模组程序集；查询命令：`dotnet --list-sdks`，当前默认 `dotnet --version` 为 10.0.302）
- Python：3.13.12（脚本运行环境，如 `script/gen_modicon.py`，依赖 Pillow）
- Node.js：v24.18.0（脚本运行环境，npm 11.6.1）
- 代码编辑器：Visual Studio Code
- 限制：RimWorld 使用的 Unity 版本为 Unity 2022.3.35（模组程序集须兼容该运行时）
- 目标框架：模组 C# 程序集须面向 **.NET Framework 4.7.2** 编译（与 RimWorld/Unity 2022.3.35 运行时匹配）
- 构建输出路径：编译生成的 DLL 存放于 `Publish\AutoTranslator\1.6\Assemblies`（按游戏版本号 1.6 分目录；RimWorld 加载对应版本程序集）
- 反编译工具：已安装命令行版 `ilspycmd` v10.1.1.8388（路径 `C:\Users\nomad\.dotnet\tools\ilspycmd.exe`）；图形界面版 ILSpy 未安装。可用 `ilspycmd <dll>` 将游戏 DLL 反编译到 `data/decompiled` 等目录
- 模组编译直接依赖本机 DLL（不进仓库，引用本机路径）：
  - RimWorld 核心：`C:\Application\steam\steamapps\common\RimWorld\RimWorldWin64_Data\Managed\Assembly-CSharp.dll`
  - UnityEngine：`C:\Application\steam\steamapps\common\RimWorld\RimWorldWin64_Data\Managed\UnityEngine.dll`
  - Harmony：`C:\Application\steam\steamapps\workshop\content\294100\2009463077`（即 `Harmony\0Harmony.dll`）
  - 注：以上均来自本机 RimWorld 安装与创意工坊，换机/重装后需重新确认路径存在。
- 路径区分大小写：Windows 下路径不区分大小写，但 RimWorld 在 Linux/macOS（及创意工坊跨平台）下按**大小写敏感**加载资源，因此 Defs/纹理/程序集的引用路径须保持大小写一致，避免跨平台找不到文件

#### VS Code 插件扩展

- **C#**（`ms-dotnettools.csharp`）：C# 语言基础支持，提供语法高亮、智能感知（IntelliSense）、代码格式化、跳转定义与调试。
- **C# Dev Kit**（`ms-dotnettools.csdevkit`）：基于 C# 扩展的解决方案/项目管理，提供解决方案资源管理器、测试资源管理器（Test Explorer）、集成的 .NET 项目构建与运行入口。
- **.NET Install Tool**（`ms-dotnettools.dotnet`）：为 VS Code 管理/自动安装所需的 .NET SDK 运行环境，是 C# / C# Dev Kit 的依赖项，确保编辑器内构建使用正确的 SDK。
- **ilspy-vscode**（`icsharpcode.ilspy-vscode`）：在 VS Code 内集成 ILSpy 反编译器，可直接浏览、反编译游戏与原版 DLL（如 `Assembly-CSharp.dll`），无需离开编辑器即可查看原版实现。
- **Unity**（`VisualStudioToolsForUnity.vstuc`，Microsoft 维护）：联调必需的调试扩展，注册 `vstuc` 调试类型，提供 Unity Debugger 可附加到 Unity Editor 与 Unity Players（RimWorld 即 Unity 构建的 Player）。**未安装时 `launch.json` 的 `type: "vstuc"` 会报「无法识别此调试类型」**。安装方式二选一：
  - 命令安装：`code --install-extension VisualStudioToolsForUnity.vstuc`（安装后需「重新加载窗口」Reload Window 使其生效）。
  - 扩展面板安装：Ctrl+Shift+X 打开扩展面板，搜索 `Unity` 或 `VisualStudioToolsForUnity`（publisher 显示 Microsoft），点击「安装」。

### 目录符号链接（mklink /D，不随 git 提交，换机需重建）

- `Extends\RimWorldMods` → `C:\Application\steam\steamapps\common\RimWorld\Mods`（本地模组）
- `Extends\RimWorldData` → `C:\Application\steam\steamapps\common\RimWorld\Data`（官方 DLC 扩展）
- `Extends\Workshop294100` → `C:\Application\steam\steamapps\workshop\content\294100`（创意工坊 Mod）
- `Extends\RimWorldDecompiled` → `C:\Users\nomad\Documents\Workspace\rimworld\RimWorldDecompiled`（官方 DLL 反编译）
- `Extends\ModTutorial` → `C:\Users\nomad\Documents\Workspace\rimworld\PatchTranslation\notes\mod教程`（Mod 教程/维基翻译）
- `C:\Application\steam\steamapps\common\RimWorld\Mods\AutoTranslator` → `C:\Users\nomad\Documents\Workspace\rimworld\AutoTranslator\Publish\AutoTranslator`（模组本体，游戏可直接识别）

### C# 工程与联调配置

- 工程位置：`Source/AutoTranslator/`（`.sln` + `AutoTranslator.csproj`，net472，输出到 `Publish\AutoTranslator\1.6\Assemblies`）
- 模组入口类：`Source/AutoTranslator/AutoTranslatorMod.cs`（`public class AutoTranslatorMod : Mod`），仅负责设置页入口（`SettingsCategory()` / `DoSettingsWindowContents()`）；不含 `[StaticConstructorOnStartup]`
- 启动初始化类：`Source/AutoTranslator/AutoTranslatorStartup.cs`（`[StaticConstructorOnStartup] public static class`），在主线程、Def/纹理就绪后做初始化（注册 Harmony、加载词库等）。两者拆分以避免 `: Mod` 过早加载时在非主线程碰纹理/Def
- 命名空间：`nomadicooer.AutoTranslator`（file-scoped 写法）；`About.xml` 的 `<packageId>` 同为 `nomadicooer.AutoTranslator`，二者一致；后续 Harmony 补丁 owner ID 也用 `nomadicooer.AutoTranslator`
- 联调（仿 ZhongHuaNamePack 配置，`.vscode/` 下三文件）：
  - `launch.json`：`Attach to RimWorld (Doorstop)`，`type:"vstuc"`，`endPoint:"127.0.0.1:56000"`，附加到 Mono 调试服务器
  - `tasks.json`：默认 `build` 任务 = `dotnet build Source/AutoTranslator/AutoTranslator.csproj -c Debug`
  - `settings.json`：`dotnet.projects.autoLoad:true` 等，确保新 .cs 纳入分析
  - 前置：游戏目录 `doorstop_config.ini` 已 `debug_enabled=true` / `debug_address=127.0.0.1:56000`，与 launch.json 匹配
  - `vstuc` 扩展已安装（`VisualStudioToolsForUnity.vstuc-1.2.2`）
  - 流程：构建 → 启动 RimWorld → F5 Attach；因 `Mods\AutoTranslator` 符号链接指向 `Publish\AutoTranslator`，DLL 自动被游戏加载

### 运行环境

RimWorld 运行在 **Unity 2022.3.35 + Mono 运行时** 之上，模组程序集（DLL）由该运行时加载执行。联调的本质是把 IDE 的调试器挂到游戏进程，对自定义程序集下断点、看堆栈。
### 开发要求  
- 尽量使用相对路径,或者引用路径变量   
- 开发阶段,不考虑兼容性问题,如果有新的逻辑,老版本代码一律更新为新的版本  
- 代码中不能出现用于显示的硬编码字符串,所有代码字符串都需要生成中英两份,字符串key存放于AutoTranslator.xml  
- RimWorld 1.6 不解析 About.xml 的 `<name>`/`<description>` 为翻译键（实测：写键名会原样显示）。Mod 元数据保持原文即可，中文名由官方语言包覆盖，不在模组自身 About.xml 处理  
- C# 代码风格：所有 .cs 一律使用 **file-scoped namespace**（C# 10+，`namespace X.Y;`），不用传统花括号包裹形式；命名空间统一为 `nomadicooer.AutoTranslator`（**含 `Models/` 等子目录下的文件也用顶层命名空间，不要再用 `.Models` 子命名空间**，否则跨文件引用需加 `using` 且 IDE 易误报 CS0246）
- 模组 ID 约定：C# 命名空间、`About.xml` 的 `<packageId>`、Harmony 补丁 owner ID 三者统一为 `nomadicooer.AutoTranslator`  
- auto.xml 约定：`TranslationPatch` 拦截 `TryTranslate` 失败时，将缺译条目写入当前激活语言包 `Languages/<当前语言>/Keyed/auto.xml`；English 作源语言基准包不写。回退 `result = key`，不调引擎、不写伪翻译。
- key 写入规则：有 key 直接以官方原 key 落盘，不加前缀；仅无 key 裸文本/UI 文本才用 `AutoKeyPrefix = "auto."` 自建主键。
- **每次改完代码都必须 git 提交**（用户明确要求）：完成一批改动后即时 `git add` 相关文件 + `git commit`，不要累积多批改动再一次性提交。提交信息用中文或中英混合、清晰描述本次改动。
- **并发约定：翻译只用「一条专属后台线程 + 队列」，禁止线程池（用户明确要求，已因此踩过卡死大坑）**。严禁在翻译链路使用 `Task.Run` / `ThreadPool.QueueUserWorkItem`：`RequestTranslate` 是每个未翻译 key 各调一次，一个界面（如 PrepareLanding）能瞬间冒出几十个 key，`Task.Run` 会自动开几十条线程池线程并发跑同步阻塞 HTTP，占满 Mono 线程池后游戏自身拿不到 worker → 整体卡死。正确做法：主线程只 `workQueue.Add()` 立即返回（这就是"异步"，UI 不阻塞），由唯一的 `AutoTranslator.Worker` 线程（`BlockingCollection.GetConsumingEnumerable()`，`IsBackground=true`）串行消费；结果经 `LongEventHandler.ExecuteWhenFinished` 交回主线程写 `keyedReplacements`。所有对外 HTTP 必须设超时（现统一 6 秒）+ 复用 static HttpClient。Unity 协程不适用于此场景：协程跑在主线程上，只是分帧续跑、并非并发，放同步阻塞调用会直接卡死主线程。
- **架构偏好：金字塔单向分层，禁止互相引用（用户明确要求）**。代码要自顶向下、上层依赖下层、下层绝不回头引用上层，人类读代码靠顺读一条线到底，不像机器能逐条回溯推理。出现 A↔B 双向依赖/互相调用（如 patch 类既拦截又被业务层回调）即视为坏味道，立即把被反向依赖的部分下沉为独立底层工具（例：`TranslationPatch` 与 `AutoTranslatorEngine` 互相引用 → 抽出 `AutoXmlWriter` 让 Engine 单向依赖它）。写代码前先想清楚分层与调用方向，不要写完等用户当 QA 点出循环引用。

