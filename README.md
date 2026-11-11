# AutoTranslator

RimWorld 1.6 的自动翻译 Mod。它处理缺少当前语言译文的 Keyed 文本、其他 Mod 的 Def 名称和描述，以及界面实际绘制的非目标语言文本。译文会写回当前游戏界面；开启翻译记录后，还会保存到当前语言的翻译文件。

目前只支持兼容 OpenAI Chat Completions 的 API，包括按该格式提供服务的 AI 中转站。使用翻译服务可能产生费用。

## 安装

1. 在 Steam 创意工坊订阅 [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2006605356)。
2. 下载仓库中的 [`Publish/AutoTranslator-1.6.zip`](Publish/AutoTranslator-1.6.zip)，解压后将 `AutoTranslator` 文件夹放入 RimWorld 的 `Mods` 目录。最终应能找到 `Mods/AutoTranslator/About/About.xml` 和 `Mods/AutoTranslator/1.6/Assemblies/AutoTranslator.dll`。
3. 在游戏的“模组”界面启用 Harmony 和 AutoTranslator，让 Harmony 排在 AutoTranslator 前面，并按提示重启游戏。
4. 将游戏语言设置为需要翻译的语言；English 是原文基准，不会触发翻译。

## 设置

在 RimWorld 的 Mod 设置中打开 AutoTranslator：

- **开启动态翻译**：总开关，默认开启。
- **启用翻译记录**：将发现的原文与译文保存到当前语言包的 `Keyed/auto.xml`，默认关闭。
- **启用实时翻译**：发现未译的可见文本时调用翻译接口，并回写当前游戏界面，默认关闭。

设置页的“清除翻译缓存”会删除当前语言由本 Mod 保存的全部翻译记录，包括 Keyed 文本、界面文本、Def 记录和生成的 Def 注入文件，同时清理运行时内存译文；之后再次出现的外语文本会重新进入翻译队列。

同一目标语言下，完全相同的原文会复用已有译文；开启翻译记录后，保存的结果可在重新启动游戏后复用。空格、换行或动态数字不同的文本会被视为不同原文。

同一原文实际请求失败三次后保持原样，不再自动重试；开启翻译记录时，这一结果会保存，清除翻译记录后可重新尝试。接口处于冷却期而未实际发出请求时，不计入失败次数。

填写 API base URL、模型名和 API Key 后保存。接口须兼容 OpenAI 的 `POST /chat/completions`；base URL 通常形如 `https://example.com/v1`。只接受 HTTPS，或指向本机回环地址的 HTTP。旧版本已保存的 OpenAI 凭据会保留，其他平台配置不再使用。

要实时翻译并保存译文，请同时打开“启用实时翻译”和“启用翻译记录”。只打开实时翻译也会请求接口并回写当前界面，但不会将新译文保存到 `auto.xml`。只打开翻译记录则会收集未译 key，不会请求接口。

设置页的“打开翻译文件”可打开当前语言包的 `auto.xml`。人工修正译文后，可以将条目标记为 `verified="true"`，防止后续自动覆盖。

其他 Mod 的 Def 名称和描述在游戏显示时按需翻译，结果通常需要重新打开对应界面才能看到。开启翻译记录后，原文和译文保存在 `DefTranslations/<语言>/auto.xml`，成功译文还会生成在 `Languages/<语言>/DefInjected/<Def类型>/AutoTranslator.xml`，下次启动时由 RimWorld 直接加载。界面文字按原文去重，存入当前语言的 `Keyed/auto.xml`，键以 `auto.ui.` 开头。混合中文和外文的界面文本也会提交给模型，由模型返回 `decision`：`成功`、`失败` 或 `不需翻译`；只有 `成功` 才替换文本，`不需翻译` 保持原样。接口返回必须是带 `decision`、`translatedText`、`reason` 的 JSON。左上角显示当前捕捉到的待翻译数及已完成/总数。首次浏览内容时可能产生较多翻译请求和费用。

## 范围与限制

- 捕捉范围包括通过 Unity IMGUI 标签、按钮、开关和框绘制的文字。图片内文字、自绘图形文字、未显示的字段，以及绕过这些绘制接口的文字无法捕捉。Def 翻译按显示触发，不会在启动时扫描全部 Mod 内容。
- 语言过滤主要依据字符脚本：简体/繁体中文界面中的纯英文及其他明显不同脚本的文字会触发翻译。含目标语言文字的混合文本会保留原样，避免把已汉化的说明和鸣谢误译；其中的外语片段可能漏译。法语、德语等同为拉丁字母的语言无法仅凭字符准确区分。来源语言按常见脚本推断，短词和纯汉字日文也可能无法识别。
- 当前语言为 English 时不会翻译或写入文件；开发模式不再阻止翻译。
- 翻译请求最多由 3 条后台线程从同一队列并行取出，并保留接口请求间隔和限流重试。界面上新出现的文本仍可能需要稍等或重新打开界面才能看到译文；并发请求会更快消耗接口额度。
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
