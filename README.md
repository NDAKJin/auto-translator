# AutoTranslator

RimWorld 1.6 的自动翻译 Mod。它在游戏找不到 keyed 文本的当前语言译文时，取英文原文交给已配置的翻译平台，并可将译文写回游戏及当前语言的 `Keyed/auto.xml`。

目前支持火山引擎、阿里云、百度翻译，以及兼容 OpenAI Chat Completions 的 AI 接口。使用翻译服务可能产生费用。

## 安装

1. 在 Steam 创意工坊订阅 [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2006605356)。
2. 下载仓库中的 [`Publish/AutoTranslator-1.6.zip`](Publish/AutoTranslator-1.6.zip)，解压后将 `AutoTranslator` 文件夹放入 RimWorld 的 `Mods` 目录。最终应能找到 `Mods/AutoTranslator/About/About.xml` 和 `Mods/AutoTranslator/1.6/Assemblies/AutoTranslator.dll`。
3. 在游戏的“模组”界面启用 Harmony 和 AutoTranslator，让 Harmony 排在 AutoTranslator 前面，并按提示重启游戏。
4. 将游戏语言设置为需要翻译的语言；English 是原文基准，不会触发翻译。

## 设置

在 RimWorld 的 Mod 设置中打开 AutoTranslator：

- **开启动态翻译**：总开关，默认开启。
- **启用翻译记录**：将发现的原文与译文保存到当前语言包的 `Keyed/auto.xml`，默认关闭。
- **启用实时翻译**：发现未翻译的 key 时调用翻译接口，并回写当前游戏界面，默认关闭。

选择并启用至少一个翻译平台，填写对应凭据。使用 AI 中转站时，还需填写 API base URL、模型名和 API Key。接口须兼容 OpenAI 的 `POST /chat/completions`；base URL 通常形如 `https://example.com/v1`。只接受 HTTPS，或指向本机回环地址的 HTTP。不确定中转站提供的地址格式时，以服务商文档为准。

要实时翻译并保存译文，请同时打开“启用实时翻译”和“启用翻译记录”。只打开实时翻译也会请求接口并回写当前界面，但不会将新译文保存到 `auto.xml`。只打开翻译记录则会收集未译 key，不会请求接口。

设置页的“打开翻译文件”可打开当前语言包的 `auto.xml`。人工修正译文后，可以将条目标记为 `verified="true"`，防止后续自动覆盖。

## 范围与限制

- 目前主要处理游戏及其他 Mod 中缺少当前语言译文的 keyed 文本；任意界面文本的拦截、设置页一键批量翻译和逐条人工核对尚未实现。
- 游戏开发模式下不会翻译或记录；当前语言为 English 时也不会翻译或写入文件。
- 翻译请求在后台单线程队列中执行，界面上新出现的文本可能需要稍等或重新打开界面才能看到译文。
- 使用时必须启用 Harmony。编译所用的 Harmony NuGet 包不会随 Mod 一起打包。

## 从源码构建

需要 .NET SDK、RimWorld 1.6 的本地游戏文件，以及能访问 NuGet 的网络连接。项目目标框架为 .NET Framework 4.7.2；所需编译引用和 Harmony 编译包由 NuGet 还原。游戏程序集不会提交到仓库。

在 PowerShell 中从仓库根目录执行：

```powershell
$rimworld = 'D:\Program Files (x86)\Steam\steamapps\common\RimWorld'
dotnet build .\Source\AutoTranslator\AutoTranslator.csproj -c Release "-p:RimWorldDir=$rimworld"
```

把 `$rimworld` 改为自己的游戏安装目录。生成的 DLL 位于 `Publish/AutoTranslator/1.6/Assemblies/AutoTranslator.dll`。需要重新生成安装包时，运行：

```powershell
Compress-Archive -LiteralPath .\Publish\AutoTranslator -DestinationPath .\Publish\AutoTranslator-1.6.zip -Force
```
