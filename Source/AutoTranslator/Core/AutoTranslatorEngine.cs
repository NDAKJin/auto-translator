using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using RimWorld;
using UnityEngine;
using Verse;

namespace nomadicooer.AutoTranslator;

/// <summary>
/// 自动翻译引擎：统一管理翻译源（启用的平台凭据）、后台异步翻译线程、
/// 把官方未翻译的 key 提交翻译并回写到 keyedReplacements / auto.xml。
/// 以新逻辑为主：落盘统一走 AutoTranslationStore（取代旧版 AutoXmlWriter），
/// 日志统一走 AutoLog（取代旧版 Logger）。
/// </summary>
public static class AutoTranslatorEngine
{
    /// <summary>翻译请求队列项。</summary>
    private sealed class TranslationTask
    {
        public string Key { get; set; }
        public string SourceLangCode { get; set; }
        public string TargetLangCode { get; set; }
        public string Text { get; set; }
    }

    // 翻译请求队列：拦截线程（主线程）入队，后台翻译线程出队。
    private static readonly ConcurrentQueue<TranslationTask> TranslationQueue = new();

    // 当前合用的翻译派发器（持有已注册、已注入凭据的平台源）；首次拦截/刷新时构建。
    private static TranslationDispatcher Dispatcher;

    // 后台翻译线程。
    private static Thread backgroundThread;

    // 引擎是否已配置并运行（有可用平台源 + 实时翻译开关开启）。
    private static volatile bool configured;

    // 后台线程是否应继续运行（控制线程退出）。
    private static volatile bool keepRunning;

    // 配置线程锁：避免刷新与拦截并发改 Dispatcher。
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

    /// <summary>后台线程主循环：从队列取任务翻译，成功回写并落盘。</summary>
    private static void RunTranslateTask()
    {
        // 等配置就绪（首次刷新前 Dispatcher 为空）。
        while (!keepRunning)
        {
            Thread.Sleep(40);
        }

        while (keepRunning)
        {
            // 未配置（无可用源或实时翻译关）时清空队列并等待，避免无谓占用。
            if (!configured || Dispatcher == null || Dispatcher.Count == 0)
            {
                while (TranslationQueue.TryDequeue(out _)) { }
                Thread.Sleep(200);
                continue;
            }

            if (!TranslationQueue.TryDequeue(out TranslationTask task))
            {
                Thread.Sleep(40);
                continue;
            }

            Translate(task);
        }
    }

    /// <summary>执行单条翻译：调派发器翻译，成功回写 keyedReplacements + 落盘 Store；失败记录原文占位。</summary>
    private static void Translate(TranslationTask task)
    {
        if (string.IsNullOrEmpty(task.Text))
        {
            return;
        }

        string sourceCode = Dispatcher.GetSourceLangCode(LanguageDatabase.defaultLanguage.folderName);
        string targetCode = Dispatcher.GetTargetLangCode(LanguageDatabase.activeLanguage.folderName);
        if (string.IsNullOrEmpty(sourceCode) || string.IsNullOrEmpty(targetCode))
        {
            AutoLog.Warn($"语言码映射失败（source={sourceCode}, target={targetCode}），跳过 {task.Key}");
            return;
        }

        TranslationResult result = Dispatcher.Translate(sourceCode, targetCode, task.Text);
        if (result.Success && !string.IsNullOrEmpty(result.TranslatedText))
        {
            string translated = SanitizeResult(task.Text, result.TranslatedText);
            WriteToRimWorld(task.Key, translated);
            AutoTranslationStore.RecordTranslation(task.Key, translated);
        }
        else
        {
            // 翻译失败：原文已通过拦截时的 RecordSource 立条，这里仅记日志，不重复入库。
            AutoLog.Warn($"翻译失败 [{task.Key}]：{result.ErrorCode} {result.RawError}");
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

    /// <summary>提交一条翻译请求（主线程拦截时调用）。</summary>
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
        TranslationQueue.Enqueue(new TranslationTask
        {
            Key = key,
            Text = text,
        });
    }

    /// <summary>一键翻译：把当前未翻译的已记录条目批量提交翻译。</summary>
    public static void TranslateAllPending()
    {
        if (!configured || Dispatcher == null || Dispatcher.Count == 0)
        {
            AutoLog.Warn("翻译源未就绪，无法一键翻译（请先在设置页启用平台并填入凭据）");
            return;
        }
        foreach ((string key, string source) in AutoTranslationStore.LoadUnTranslated())
        {
            RequestTranslate(key, source);
        }
    }

    /// <summary>刷新翻译源并据开关启动/停止引擎。设置页保存或启动初始化时调用；无凭据时已安全空源，无需重启。</summary>
    public static void RefreshSources()
    {
        lock (configLock)
        {
            BuildSources();
            bool shouldRun = AutoTranslatorMod.AutoTranslateEnabled
                             && AutoTranslatorMod.RealtimeUpdateEnabled
                             && Dispatcher != null
                             && Dispatcher.Count > 0;

            if (shouldRun)
            {
                configured = true;
                if (backgroundThread == null)
                {
                    keepRunning = true;
                    backgroundThread = new Thread(RunTranslateTask) { IsBackground = true, Name = "AutoTranslator" };
                    backgroundThread.Start();
                    AutoLog.Info("自动翻译引擎已启动");
                }
            }
            else
            {
                configured = false;
                Stop();
                AutoLog.Info("自动翻译引擎已停止（无可用平台或实时翻译关闭）");
            }
        }
    }

    /// <summary>停止后台线程（仅停止运行，保留 Dispatcher/配置以便再次启动）。</summary>
    private static void Stop()
    {
        keepRunning = false;
        backgroundThread = null;
    }
}
