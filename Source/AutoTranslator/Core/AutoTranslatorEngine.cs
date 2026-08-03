using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RimWorld;
using Verse;

namespace nomadicooer.AutoTranslator;

/// <summary>
/// 自动翻译引擎：统一管理翻译源（启用的平台凭据）、按需异步翻译（Task.Run，不阻塞主线程），
/// 把官方未翻译的 key 提交翻译并回写到 keyedReplacements / auto.xml。
/// 落盘统一走 AutoTranslationStore，日志统一走 AutoLog。
/// 不常驻线程：主线程拦截到漏译时提交一条异步任务，由线程池后台执行翻译并回写。
/// </summary>
public static class AutoTranslatorEngine
{
    // 当前合用的翻译派发器（持有已注册、已注入凭据的平台源）；首次拦截/刷新时构建。
    private static TranslationDispatcher Dispatcher;

    // 引擎是否已配置并运行（有可用平台源 + 实时翻译开关开启）。
    private static volatile bool configured;

    // 配置锁：避免刷新与拦截并发改 Dispatcher。
    private static readonly object configLock = new();

    /// <summary>构建或重建翻译源：把设置里已启用的平台凭据注入对应平台实例并注册到派发器。</summary>
    private static void BuildSources()
    {
        if (AutoTranslatorMod.Settings == null)
        {
            Dispatcher = new TranslationDispatcher();
            return;
        }

        var dispatcher = new TranslationDispatcher();
        foreach (PlatformCredential cred in AutoTranslatorMod.Settings.credentials)
        {
            if (string.IsNullOrEmpty(cred.PlatformId))
            {
                continue;
            }
            if (!PlatformRegistry.PlatformTypes.TryGetValue(cred.PlatformId, out System.Type type))
            {
                continue;
            }
            if (!CreatePlatform(type, cred.AccessKey, cred.SecretKey, out TranslatePlatform platform))
            {
                AutoLog.Warn($"平台 {cred.PlatformId} 实例化失败，已跳过");
                continue;
            }
            dispatcher.Register(platform);
        }

        Dispatcher = dispatcher;
        AutoLog.Info($"翻译源构建完成：{Dispatcher.Count} 个可用平台");
    }

    /// <summary>反射构造平台实例（访问键/密钥），失败返回 false。</summary>
    private static bool CreatePlatform(
        System.Type type, string accessKey, string secretKey, out TranslatePlatform platform)
    {
        platform = null;
        try
        {
            var obj = System.Activator.CreateInstance(type, accessKey ?? string.Empty, secretKey ?? string.Empty);
            if (obj is TranslatePlatform p)
            {
                platform = p;
                return true;
            }
        }
        catch (System.Exception ex)
        {
            AutoLog.Warn($"构造平台 {type.Name} 失败: {ex.Message}");
        }
        return false;
    }

    /// <summary>执行单条翻译：调派发器翻译，成功回写 keyedReplacements + 落盘 Store；失败记录原文占位。在线程池后台执行。</summary>
    private static void Translate(string key, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        TranslationDispatcher dispatcher = Dispatcher;
        if (dispatcher == null || dispatcher.Count == 0)
        {
            return;
        }

        string sourceCode = dispatcher.GetSourceLangCode(LanguageDatabase.defaultLanguage.folderName);
        string targetCode = dispatcher.GetTargetLangCode(LanguageDatabase.activeLanguage.folderName);
        if (string.IsNullOrEmpty(sourceCode) || string.IsNullOrEmpty(targetCode))
        {
            AutoLog.Warn($"语言码映射失败（source={sourceCode}, target={targetCode}），跳过 {key}");
            return;
        }

        TranslationResult result = dispatcher.Translate(sourceCode, targetCode, text);
        if (result.Success && !string.IsNullOrEmpty(result.TranslatedText))
        {
            string translated = SanitizeResult(text, result.TranslatedText);
            WriteToRimWorld(key, translated);
            AutoTranslationStore.RecordTranslation(key, translated);
        }
        else
        {
            // 翻译失败：原文已通过拦截时的 RecordSource 立条，这里仅记日志，不重复入库。
            AutoLog.Warn($"翻译失败 [{key}]：{result.ErrorCode} {result.RawError}");
        }
    }

    /// <summary>回写翻译结果到 RimWorld 运行时 keyedReplacements，使当前界面即时显示译文。</summary>
    private static void WriteToRimWorld(string key, string translated)
    {
        LongEventHandler.ExecuteWhenFinished(() =>
        {
            try
            {
                LoadedLanguage active = LanguageDatabase.activeLanguage;
                if (active == null || active.keyedReplacements == null)
                {
                    return;
                }
                active.keyedReplacements[key] = new LoadedLanguage.KeyedReplacement
                {
                    key = key,
                    value = translated,
                };
            }
            catch (System.Exception ex)
            {
                AutoLog.Warn($"回写 keyedReplacements 失败 [{key}]", ex);
            }
        });
    }

    /// <summary>校验译文未破坏官方占位符（{0}、{ref:X} 等），破坏则回退原文，避免界面显示异常。</summary>
    private static string SanitizeResult(string original, string translated)
    {
        if (string.IsNullOrEmpty(translated))
        {
            return original;
        }
        System.Text.RegularExpressions.MatchCollection origTags =
            System.Text.RegularExpressions.Regex.Matches(original, @"\{[^}]*\}");
        System.Text.RegularExpressions.MatchCollection transTags =
            System.Text.RegularExpressions.Regex.Matches(translated, @"\{[^}]*\}");
        if (origTags.Count != transTags.Count)
        {
            return original;
        }
        return translated;
    }

    /// <summary>提交一条翻译请求（主线程拦截时调用）。后台 Task.Run 执行，不阻塞主线程。</summary>
    public static void RequestTranslate(string key, string text)
    {
        if (!configured || Dispatcher == null || Dispatcher.Count == 0)
        {
            return;
        }
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(text))
        {
            return;
        }

        string capturedKey = key;
        string capturedText = text;
        Task.Run(() => Translate(capturedKey, capturedText));
    }

    /// <summary>一键翻译：把当前未翻译的已记录条目批量提交翻译（后台异步执行，不阻塞主线程）。</summary>
    public static void TranslateAllPending()
    {
        if (!configured || Dispatcher == null || Dispatcher.Count == 0)
        {
            AutoLog.Warn("翻译源未就绪，无法一键翻译（请先在设置页启用平台并填入凭据）");
            return;
        }

        Task.Run(() =>
        {
            foreach ((string key, string source) in AutoTranslationStore.LoadUnTranslated())
            {
                Translate(key, source);
            }
        });
    }

    /// <summary>刷新翻译源：重建派发器并据开关同步配置标志。按需异步，无需常驻线程；设置页保存或启动初始化时调用。</summary>
    public static void RefreshSources()
    {
        lock (configLock)
        {
            BuildSources();
            bool shouldRun = AutoTranslatorMod.AutoTranslateEnabled
                             && AutoTranslatorMod.RealtimeUpdateEnabled
                             && Dispatcher != null
                             && Dispatcher.Count > 0;

            configured = shouldRun;
            AutoLog.Info(shouldRun
                ? $"翻译源就绪，引擎已启用（{Dispatcher.Count} 个可用平台）"
                : "翻译源未就绪，引擎已停用（无可用平台或实时翻译关闭）");
        }
    }
}
