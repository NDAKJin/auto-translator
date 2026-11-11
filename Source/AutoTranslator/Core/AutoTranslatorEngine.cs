using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using RimWorld;
using Verse;

namespace nomadicooer.AutoTranslator;

/// <summary>
/// 自动翻译引擎：统一管理翻译源（启用的平台凭据）、按需翻译，
/// 把官方未翻译的 key 提交翻译并回写到 keyedReplacements / auto.xml。
/// 落盘统一走 AutoTranslationStore，日志统一走 AutoLog。
/// 翻译由有限数量的专属后台线程执行（不用线程池），不阻塞 UI；异常统一抛回主线程输出完整堆栈。
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
    private static readonly ConcurrentDictionary<string, int> inFlight = new();
    private static readonly object cacheLock = new();
    private static int cacheGeneration;
    private static string loadedCacheLanguage;
    private static readonly ConcurrentDictionary<string, Lazy<TranslationResult>> sharedRequests = new();
    private static readonly TranslationFailureTracker failureTracker = new();

    /// <summary>队列积压上限：超出则丢弃新请求，避免打开大界面时无限堆积。</summary>
    private const int MaxQueueLength = 200;
    private const int WorkerCount = 3;

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

    // 翻译任务队列 + 有界专属工作线程，不占用 Mono 线程池。
    private static readonly BlockingCollection<WorkItem> workQueue = new();
    private static readonly object workerLock = new();
    private static volatile bool workerStarted;

    /// <summary>用 OpenAI 兼容接口配置构建翻译源。</summary>
    private static void BuildSources()
    {
        // Each failed API call counts toward the per-text limit; do not retry 429 inside a call.
        var dispatcher = new TranslationDispatcher { MaxRetryOnRateLimit = 0 };
        PlatformCredential cred = AutoTranslatorMod.Settings?.GetCredential(OpenAICompatibleTranslate.PlatformNameKey);
        if (cred != null)
        {
            if (string.IsNullOrWhiteSpace(cred.AccessKey) || string.IsNullOrWhiteSpace(cred.Model)
                || !OpenAICompatibleTranslate.TryCreateEndpoint(cred.ApiBaseUrl, out _))
            {
                AutoLog.Warn("AI 中转站配置不完整或接口地址无效，请检查 API Key、接口地址和模型名");
            }
            else
            {
                dispatcher.Register(new OpenAICompatibleTranslate(cred.AccessKey, cred.ApiBaseUrl, cred.Model));
            }
        }
        Dispatcher = dispatcher;
        AutoLog.Info($"翻译源构建完成：{Dispatcher.Count} 个可用平台");
    }

    /// <summary>执行单条翻译：调派发器翻译，成功回写 keyedReplacements + 落盘 Store；失败记日志。运行在专属后台线程。</summary>
    private static void Translate(string key, string text, int generation)
    {
        if (generation != Volatile.Read(ref cacheGeneration) || string.IsNullOrEmpty(text))
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
        TranslationResult result = TranslateShared(dispatcher,
            LanguageDatabase.defaultLanguage.folderName,
            LanguageDatabase.activeLanguage.folderName,
            text, generation);
        lock (cacheLock)
        {
            if (generation != cacheGeneration) return;
            if (result.Success && !string.IsNullOrEmpty(result.TranslatedText))
            {
                string translated = SanitizeResult(text, result.TranslatedText);
                WriteToRimWorld(key, translated, generation);
                AutoTranslationStore.RecordTranslation(key, translated);
            }
            else
            {
                AutoLog.Warn($"翻译失败 [{key}]：{result.ErrorCode} {result.RawError}");
            }
        }
    }

    /// <summary>回写翻译结果到 RimWorld 运行时 keyedReplacements，使当前界面即时显示译文。</summary>
    private static void WriteToRimWorld(string key, string translated, int generation)
    {
        LongEventHandler.ExecuteWhenFinished(() =>
        {
            if (generation != Volatile.Read(ref cacheGeneration)) return;
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
    /// 提交一段翻译工作到队列，由有限数量的后台线程执行。
    /// 翻译是同步阻塞 HTTP，一个界面可能瞬间冒出几十个 key；线程数量保持固定，
    /// 避免占满游戏线程资源。异常捕获后转交主线程输出完整堆栈。
    /// </summary>
    private static bool RunInBackground(string context, Action work)
    {
        if (workQueue.Count >= MaxQueueLength)
        {
            // 队列积压说明网络慢或界面文本过多，丢弃新请求而非无限堆积（下次界面重入会再次提交）。
            AutoLog.Warn($"[AutoTranslator] 翻译队列已满（{MaxQueueLength}），丢弃请求 [{context}]");
            return false;
        }
        TranslateStats.TaskSubmitted();
        workQueue.Add(new WorkItem(context, work));
        EnsureWorkersStarted();
        return true;
    }

    /// <summary>按需启动固定数量的后台工作线程，它们不阻止游戏退出。</summary>
    private static void EnsureWorkersStarted()
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
            for (int i = 0; i < WorkerCount; i++)
            {
                Thread worker = new(WorkerLoop)
                {
                    IsBackground = true,
                    Name = "AutoTranslator.Worker." + (i + 1),
                };
                worker.Start();
            }
            workerStarted = true;
            AutoLog.Info($"翻译后台线程已启动：{WorkerCount} 条");
        }
    }

    /// <summary>后台工作线程主循环：逐项取出翻译任务，异常转交主线程。</summary>
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
        int generation = Volatile.Read(ref cacheGeneration);
        if (!inFlight.TryAdd(key, generation))
        {
            return;
        }

        string capturedKey = key;
        string capturedText = text;
        if (!RunInBackground(capturedKey, () =>
        {
            try
            {
                Translate(capturedKey, capturedText, generation);
            }
            finally
            {
                ReleaseInFlight(capturedKey, generation);
            }
        }))
        {
            ReleaseInFlight(capturedKey, generation);
        }
    }

    public static void RequestDefTranslate(string key, string source, string from, string to, Action<string> onTranslated)
    {
        int generation = Volatile.Read(ref cacheGeneration);
        if (!configured || Dispatcher == null || Dispatcher.Count == 0
            || string.IsNullOrWhiteSpace(source) || !inFlight.TryAdd(key, generation)) return;

        TranslationDispatcher dispatcher = Dispatcher;
        if (!RunInBackground(key, () =>
        {
            try
            {
                if (generation != Volatile.Read(ref cacheGeneration)) return;
                TranslationResult result = TranslateShared(dispatcher, from, to, source, generation);
                if (generation != Volatile.Read(ref cacheGeneration)) return;
                if (!result.Success || string.IsNullOrWhiteSpace(result.TranslatedText))
                {
                    AutoLog.Warn($"Def 翻译失败 [{key.Replace('\0', '.')}]：{result.ErrorCode} {result.RawError}");
                    return;
                }
                if (!PreservesPlaceholders(source, result.TranslatedText))
                {
                    AutoLog.Warn($"Def 译文改变了占位符 [{key.Replace('\0', '.')}]");
                    return;
                }
                string translated = result.TranslatedText;
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    if (generation == Volatile.Read(ref cacheGeneration)) onTranslated(translated);
                });
            }
            finally
            {
                ReleaseInFlight(key, generation);
            }
        }))
        {
            ReleaseInFlight(key, generation);
        }
    }

    public static bool RequestVisibleTranslate(string key, string source, string target, Action<TranslationResult> onTranslated)
    {
        if (!configured || Dispatcher == null || Dispatcher.Count == 0
            || string.IsNullOrWhiteSpace(source)) return false;

        string requestKey = target + "\0" + key;
        int generation = Volatile.Read(ref cacheGeneration);
        if (!inFlight.TryAdd(requestKey, generation)) return false;

        TranslationDispatcher dispatcher = Dispatcher;
        TranslationProgress.Enqueue(source);
        if (!RunInBackground(requestKey, () =>
        {
            try
            {
                if (generation != Volatile.Read(ref cacheGeneration)) return;
                TranslationResult result = TranslateShared(dispatcher,
                    VisibleTextFilter.GuessSource(source), target, source, generation);
                if (generation != Volatile.Read(ref cacheGeneration)) return;
                if (!result.Success || (result.Decision == TranslationDecision.Success
                    && string.IsNullOrWhiteSpace(result.TranslatedText)))
                {
                    AutoLog.Warn($"界面翻译失败 [{key}]：{result.ErrorCode} {result.RawError}");
                    return;
                }
                if (result.Decision == TranslationDecision.Success
                    && !PreservesPlaceholders(source, result.TranslatedText))
                {
                    AutoLog.Warn($"界面译文改变了占位符 [{key}]");
                    return;
                }
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    if (generation == Volatile.Read(ref cacheGeneration)) onTranslated(result);
                });
            }
            finally
            {
                // Keep it pending until the main-thread callback records either the
                // translated text or the model's "不需翻译" decision.
                if (generation == Volatile.Read(ref cacheGeneration)) TranslationProgress.Finish(source, false);
                ReleaseInFlight(requestKey, generation);
            }
        }))
        {
            if (generation == Volatile.Read(ref cacheGeneration)) TranslationProgress.Finish(source, false);
            ReleaseInFlight(requestKey, generation);
            return false;
        }
        return true;
    }

    private static bool PreservesPlaceholders(string source, string translation)
    {
        var counts = new Dictionary<string, int>();
        foreach (Match match in Regex.Matches(source, @"\{[^}]*\}"))
        {
            counts.TryGetValue(match.Value, out int count);
            counts[match.Value] = count + 1;
        }
        foreach (Match match in Regex.Matches(translation, @"\{[^}]*\}"))
        {
            if (!counts.TryGetValue(match.Value, out int count) || count == 0) return false;
            counts[match.Value] = count - 1;
        }
        foreach (int count in counts.Values)
        {
            if (count != 0) return false;
        }
        return true;
    }

    /// <summary>一键翻译：把当前未翻译的已记录条目批量提交翻译（后台异步执行，不阻塞主线程）。</summary>
    public static void TranslateAllPending()
    {
        if (!configured || Dispatcher == null || Dispatcher.Count == 0)
        {
            AutoLog.Info("翻译源未就绪，无法一键翻译（请先在设置页填写并保存接口配置）");
            return;
        }

        var pending = new Queue<(string Key, string Source)>(AutoTranslationStore.LoadUnTranslated());
        int generation = Volatile.Read(ref cacheGeneration);
        int initialTasks = Math.Min(WorkerCount, pending.Count);
        for (int i = 0; i < initialTasks; i++) EnqueuePendingBatchItem(pending, generation);
    }

    private static void EnqueuePendingBatchItem(Queue<(string Key, string Source)> pending, int generation)
    {
        if (generation != Volatile.Read(ref cacheGeneration)) return;
        (string Key, string Source) item;
        lock (pending)
        {
            if (pending.Count == 0) return;
            item = pending.Dequeue();
        }

        // Each completed item queues one successor, so bulk work shares the same queue
        // with newly captured UI text without submitting hundreds of tasks at once.
        TranslateStats.TaskSubmitted();
        workQueue.Add(new WorkItem(item.Key, () =>
        {
            try
            {
                if (generation != Volatile.Read(ref cacheGeneration)
                    || AutoTranslationStore.IsTranslated(item.Key)
                    || !inFlight.TryAdd(item.Key, generation)) return;
                try { Translate(item.Key, item.Source, generation); }
                finally { ReleaseInFlight(item.Key, generation); }
            }
            finally
            {
                EnqueuePendingBatchItem(pending, generation);
            }
        }));
        EnsureWorkersStarted();
    }

    private static void ReleaseInFlight(string key, int generation) =>
        ((ICollection<KeyValuePair<string, int>>)inFlight).Remove(new KeyValuePair<string, int>(key, generation));

    public static void EnsureSourceCacheLoaded(string target)
    {
        if (loadedCacheLanguage == target) return;
        AutoTranslationStore.LoadSourceTranslations(target);
        DefTranslationStore.LoadSourceTranslations(target);
        loadedCacheLanguage = target;
    }

    private static TranslationResult TranslateShared(TranslationDispatcher dispatcher, string from, string target, string source, int generation)
    {
        if (SourceTranslationCache.TryGet(target, source, out string cached))
            return TranslationResult.Ok(cached);
        string requestKey = generation + "\0" + target + "\0" + source;
        var request = sharedRequests.GetOrAdd(requestKey, _ => new Lazy<TranslationResult>(() =>
        {
            if (SourceTranslationCache.TryGet(target, source, out string existing)) return TranslationResult.Ok(existing);
            TranslationResult result = dispatcher.Translate(from, target, source);
            lock (cacheLock)
            {
                if (generation != cacheGeneration) return result;
                if (!result.Success)
                {
                    // A dispatcher in cooldown did not contact the API, so it is not an attempt.
                    if (result.RawError == "所有翻译源均不可用") return result;
                    if (failureTracker.Record(target, source) < TranslationFailureTracker.Limit) return result;
                    AutoLog.Warn("同一文本翻译失败 3 次，保持原文并停止重试。");
                    string unchanged = SourceTranslationCache.Remember(target, source, source);
                    return unchanged == source ? TranslationResult.NotNeeded(source, "失败次数达到上限")
                        : TranslationResult.Ok(unchanged);
                }
                string value = result.Decision == TranslationDecision.NotNeeded ? source : result.TranslatedText;
                if (string.IsNullOrEmpty(value)) return result;
                string chosen = SourceTranslationCache.Remember(target, source, value);
                return result.Decision == TranslationDecision.NotNeeded && chosen == source
                    ? TranslationResult.NotNeeded(source, result.RawError) : TranslationResult.Ok(chosen);
            }
        }, LazyThreadSafetyMode.ExecutionAndPublication));
        try { return request.Value; }
        finally { ((ICollection<KeyValuePair<string, Lazy<TranslationResult>>>)sharedRequests).Remove(new KeyValuePair<string, Lazy<TranslationResult>>(requestKey, request)); }
    }

    /// <summary>清除当前语言的全部翻译记录和运行时译文：Keyed、界面文本、Def 记录、Def 注入及内存缓存。</summary>
    public static void ClearTranslationCache()
    {
        lock (cacheLock)
        {
            Interlocked.Increment(ref cacheGeneration);
            SourceTranslationCache.Clear();
            failureTracker.Clear();
            loadedCacheLanguage = null;
            while (workQueue.TryTake(out _)) TranslateStats.TaskFinished();
            inFlight.Clear();
            List<DefTranslationRecord> defs = DefTranslationStore.ClearCurrentLanguage();
            Dictionary<string, string> keyed = AutoTranslationStore.ClearCurrentLanguage();
            LoadedLanguage active = LanguageDatabase.activeLanguage;
            if (active?.keyedReplacements != null)
            {
                foreach (KeyValuePair<string, string> entry in keyed)
                {
                    if (active.keyedReplacements.TryGetValue(entry.Key, out LoadedLanguage.KeyedReplacement current)
                        && current.value == entry.Value) active.keyedReplacements.Remove(entry.Key);
                }
            }
            DefTranslationPatch.ClearCache(defs);
            VisibleTextTranslations.ClearCache();
        }
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
