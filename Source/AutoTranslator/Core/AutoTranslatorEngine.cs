using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using RimWorld;
using Verse;

namespace nomadicooer.AutoTranslator;

/// <summary>
/// 自动翻译引擎：统一管理翻译源（启用的平台凭据）、按需翻译，
/// 把官方未翻译的 key 提交翻译并回写到 keyedReplacements / auto.xml。
/// 落盘统一走 AutoTranslationStore，日志统一走 AutoLog。
/// 翻译由一条专属后台线程串行执行（不用线程池），不阻塞 UI；异常不吞，统一抛回主线程输出完整堆栈。
/// </summary>
public static class AutoTranslatorEngine
{
    // 当前合用的翻译派发器（持有已注册、已注入凭据的平台源）；首次拦截/刷新时构建。
    private static TranslationDispatcher Dispatcher;

    // 引擎是否已配置并运行（有可用平台源 + 实时翻译开关开启）。
    private static volatile bool configured;

    // 配置锁：避免刷新与拦截并发改 Dispatcher。
    private static readonly object configLock = new();

    // 正在后台翻译中的 key（去重用）：Postfix 每帧重入同一 key，任务未回来前不重复提交请求。
    private static readonly ConcurrentDictionary<string, byte> inFlight = new();

    /// <summary>队列积压上限：超出则丢弃新请求，避免打开大界面时无限堆积。</summary>
    private const int MaxQueueLength = 200;

    /// <summary>一条待执行的后台翻译任务。</summary>
    private sealed class WorkItem
    {
        public WorkItem(string context, Action work)
        {
            Context = context;
            Work = work;
        }

        public string Context { get; }

        public Action Work { get; }
    }

    // 翻译任务队列 + 专属单工作线程：串行执行，绝不占用 Mono 线程池。
    private static readonly BlockingCollection<WorkItem> workQueue = new();
    private static readonly object workerLock = new();
    private static volatile bool workerStarted;

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
            if (string.IsNullOrEmpty(cred.PlatformId) || !cred.enabled)
            {
                continue;
            }
            if (!PlatformRegistry.PlatformTypes.TryGetValue(cred.PlatformId, out System.Type type))
            {
                continue;
            }
            if (!CreatePlatform(type, cred, out TranslatePlatform platform))
            {
                AutoLog.Warn($"平台 {cred.PlatformId} 实例化失败，已跳过");
                continue;
            }
            dispatcher.Register(platform);
        }

        Dispatcher = dispatcher;
        AutoLog.Info($"翻译源构建完成：{Dispatcher.Count} 个可用平台");
    }

    /// <summary>按平台配置构造翻译源，配置不完整时跳过。</summary>
    private static bool CreatePlatform(
        System.Type type, PlatformCredential cred, out TranslatePlatform platform)
    {
        platform = null;
        if (type == typeof(OpenAICompatibleTranslate))
        {
            if (string.IsNullOrWhiteSpace(cred.AccessKey) || string.IsNullOrWhiteSpace(cred.Model)
                || !OpenAICompatibleTranslate.TryCreateEndpoint(cred.ApiBaseUrl, out _))
            {
                AutoLog.Warn("AI 中转站配置不完整或接口地址无效，请检查 API Key、接口地址和模型名");
                return false;
            }
            platform = new OpenAICompatibleTranslate(cred.AccessKey, cred.ApiBaseUrl, cred.Model);
            return true;
        }
        var obj = System.Activator.CreateInstance(type, cred.AccessKey ?? string.Empty, cred.SecretKey ?? string.Empty);
        if (obj is TranslatePlatform p)
        {
            platform = p;
            return true;
        }
        return false;
    }

    /// <summary>执行单条翻译：调派发器翻译，成功回写 keyedReplacements + 落盘 Store；失败记日志。运行在线程池后台线程。</summary>
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

        // 直接传 RimWorld 语言名（folderName），语言码映射交由底层 TranslatePlatform.GetLangCode 统一处理，
        // 不要在此预先转成平台码：dispatcher.Translate 内部及模板方法期望收到的是 RimWorld 名，
        // 二次转换会导致平台码被当 RimWorld 名查表失败、TranslateCore 永不执行。
        TranslationResult result = dispatcher.Translate(
            LanguageDatabase.defaultLanguage.folderName,
            LanguageDatabase.activeLanguage.folderName,
            text);
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

    /// <summary>
    /// 提交一段翻译工作到队列，由那一条后台线程串行执行。
    /// 翻译是同步阻塞 HTTP，一个界面可能瞬间冒出几十个 key，因此只用自己的一条线程排队做，
    /// 不去抢游戏的线程资源。异常不吞，捕获后转交主线程输出完整堆栈。
    /// </summary>
    private static void RunInBackground(string context, Action work)
    {
        if (workQueue.Count >= MaxQueueLength)
        {
            // 队列积压说明网络慢或界面文本过多，丢弃新请求而非无限堆积（下次界面重入会再次提交）。
            AutoLog.Warn($"[AutoTranslator] 翻译队列已满（{MaxQueueLength}），丢弃请求 [{context}]");
            return;
        }
        TranslateStats.TaskSubmitted();
        workQueue.Add(new WorkItem(context, work));
        EnsureWorkerStarted();
    }

    /// <summary>确保后台工作线程已启动（懒启动，后台线程不阻止游戏退出）。</summary>
    private static void EnsureWorkerStarted()
    {
        if (workerStarted)
        {
            return;
        }
        lock (workerLock)
        {
            if (workerStarted)
            {
                return;
            }
            workerStarted = true;
            Thread worker = new(WorkerLoop)
            {
                IsBackground = true,
                Name = "AutoTranslator.Worker",
            };
            worker.Start();
        }
    }

    /// <summary>后台工作线程主循环：串行取出翻译任务执行，异常不吞、转交主线程。</summary>
    private static void WorkerLoop()
    {
        foreach (WorkItem item in workQueue.GetConsumingEnumerable())
        {
            try
            {
                item.Work();
            }
            catch (Exception ex)
            {
                ReportToMainThread(item.Context, ex);
            }
            finally
            {
                TranslateStats.TaskFinished();
                // 队列排空即本轮翻译结束，输出一次统计便于核对是否还有多余请求。
                if (workQueue.Count == 0 && TranslateStats.RunningTasks == 0)
                {
                    TranslateStats.LogSummary();
                }
            }
        }
    }

    /// <summary>把后台线程捕获的异常投递到主线程，用 RimWorld 日志输出完整堆栈。</summary>
    private static void ReportToMainThread(string context, Exception ex)
    {
        Exception captured = ex;
        string capturedContext = context;
        LongEventHandler.ExecuteWhenFinished(() =>
        {
            AutoLog.Error($"后台翻译异常 [{capturedContext}]：{captured}");
        });
    }

    /// <summary>提交一条翻译请求（主线程拦截时调用）：投入队列由后台线程执行，不阻塞主线程。</summary>
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
        // 已译兜底：Postfix 每帧都会重入同一 key，若已有译文则绝不再提交后台请求，
        // 避免「翻译完了还在后台刷接口」。
        if (AutoTranslationStore.IsTranslated(key))
        {
            return;
        }
        // 同一 key 已有后台任务在跑时不重复提交（Postfix 每帧重入，任务未回来前会反复提交同一条）。
        if (!inFlight.TryAdd(key, 0))
        {
            return;
        }

        string capturedKey = key;
        string capturedText = text;
        RunInBackground(capturedKey, () =>
        {
            try
            {
                Translate(capturedKey, capturedText);
            }
            finally
            {
                inFlight.TryRemove(capturedKey, out _);
            }
        });
    }

    /// <summary>一键翻译：把当前未翻译的已记录条目批量提交翻译（后台异步执行，不阻塞主线程）。</summary>
    public static void TranslateAllPending()
    {
        if (!configured || Dispatcher == null || Dispatcher.Count == 0)
        {
            AutoLog.Info("翻译源未就绪，无法一键翻译（请先在设置页启用平台并填入凭据）");
            return;
        }

        RunInBackground("TranslateAllPending", () =>
        {
            foreach ((string key, string source) in AutoTranslationStore.LoadUnTranslated())
            {
                Translate(key, source);
            }
        });
    }

    /// <summary>在系统默认程序中打开当前语言包 auto.xml（外部人工校对用）。路径无效（English/未就绪）时仅记日志。
    /// 打开前强制 Flush 一次（dirtyCount==0 时直接返回，安全）确保文件已落盘，避免打开不存在的文件。</summary>
    public static void OpenAutoXml()
    {
        AutoTranslationStore.Flush();
        string path = AutoTranslationStore.GetAutoXmlPath();
        if (string.IsNullOrEmpty(path))
        {
            AutoLog.Warn("当前语言为 English 或语言尚未就绪，无 auto.xml 可打开");
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch (System.Exception ex)
        {
            AutoLog.Error($"打开 auto.xml 失败：{path}\n{ex}");
        }
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
