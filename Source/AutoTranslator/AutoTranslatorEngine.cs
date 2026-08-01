using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Verse;

namespace nomadicooer.AutoTranslator;

/// <summary>
/// 自动翻译引擎（消费层，异步）。
/// 主线程遇到未翻译 key 时调用 <see cref="RequestTranslate"/> 入队（不阻塞）；
/// 后台线程从队列取任务，调用 <see cref="TranslationDispatcher"/>（其内部为同步阻塞实现）翻译，
    /// 拿到译文后：
    ///   1) 写进 RimWorld 当前激活语言的 keyedReplacements 字典（实时生效，下次查询该 key 即命中，无需重载语言包）；
    ///   2) 存入内存 cache（已翻译的 key 不再请求 API）；
    ///   3) 写 auto.xml：翻译成功写译文，失败写原文（fallback）。
    /// 已翻译的 key 通过 cache + pending 双层去重，绝不重复打 API。
/// 切换语言概率极低，未做完整兼容：后台线程检测到 activeLanguage 已变则跳过回写（不崩，仅该批译文不生效）。
/// </summary>
public static class AutoTranslatorEngine
{
    private sealed class Job
    {
        public string Key;
        public string Source;
        public string Target;
        public string Fallback; // 翻译失败时的回退原文（写 auto.xml 用）
    }

    // 待翻译队列：后台线程从此处取任务。BlockingCollection 在 .NET Framework 4.0+ 可用。
    private static readonly BlockingCollection<Job> Queue = new BlockingCollection<Job>();

    // 已翻译结果缓存：原文 key -> 译文。后台线程写、主线程（经 TryTranslate 命中）间接读。
    private static readonly ConcurrentDictionary<string, string> Cache =
        new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

    // 在途去重：译文回来前同一 key 只入队一次。
    private static readonly HashSet<string> Pending = new HashSet<string>(StringComparer.Ordinal);
    private static readonly object PendingLock = new object();

    // 保护 keyedReplacements 的并发读写（后台线程写、RimWorld 主线程经 TryGetTextFromKey 读）。
    private static readonly object DictLock = new object();

    private static TranslationDispatcher _dispatcher;
    private static Thread _worker;
    private static bool _running;

    // 绑定回写的语言 folderName；切语言后据此跳过回写，避免写错语言对象。
    private static string _boundFolderName;

    /// <summary>
    /// 注入已注册好翻译源的调度器并启动后台消费线程。
    /// 应在主线程初始化阶段（如 <see cref="AutoTranslatorStartup"/>）调用一次。
    /// </summary>
    /// <param name="dispatcher">已 new 好翻译源并 Register 的调度器实例。</param>
    public static void Configure(TranslationDispatcher dispatcher)
    {
        if (_running)
        {
            return;
        }

        _dispatcher = dispatcher;
        if (_dispatcher == null)
        {
            Logger.Warning("翻译调度器为空，自动翻译引擎未启动。");
            return;
        }

        _boundFolderName = LanguageDatabase.activeLanguage?.folderName;
        _running = true;
        _worker = new Thread(ConsumeLoop)
        {
            IsBackground = true,
            Name = "AutoTranslator.Worker"
        };
        _worker.Start();
        Logger.Message("自动翻译引擎已启动（后台消费线程）。");
    }

    /// <summary>
    /// 提交一条翻译请求。立即返回，不阻塞主线程。
    /// 已翻译（命中 cache）或在途（命中 pending）的 key 会被去重，不重复打 API。
    /// 引擎未启动（未 Configure）时直接忽略。
    /// </summary>
    /// <param name="key">未翻译的翻译键（即 RimWorld 原文文本）。</param>
    /// <param name="source">源语言（RimWorld 语言文件夹名，如 "English"）。</param>
    /// <param name="target">目标语言（RimWorld 语言文件夹名，如 "ChineseSimplified (简体中文)"）。</param>
    /// <param name="fallback">翻译失败时的回退原文，用于写 auto.xml。</param>
    public static void RequestTranslate(string key, string source, string target, string fallback)
    {
        if (!_running || key == null)
        {
            return;
        }

        if (Cache.ContainsKey(key))
        {
            return; // 已翻译，直接返回，不请求 API
        }

        lock (PendingLock)
        {
            if (Pending.Contains(key))
            {
                return; // 已在途中，不重复请求
            }

            Pending.Add(key);
        }

        Queue.Add(new Job { Key = key, Source = source, Target = target, Fallback = fallback });
    }

    private static void ConsumeLoop()
    {
        foreach (Job job in Queue.GetConsumingEnumerable())
        {
            bool ok = false;
            string translated = null;
            try
            {
                TranslationResult result = _dispatcher.Translate(job.Source, job.Target, job.Key);
                if (result.Success && !string.IsNullOrEmpty(result.TranslatedText))
                {
                    ok = true;
                    translated = result.TranslatedText;
                    Cache[job.Key] = translated;
                    WriteToRimWorld(job.Key, translated);
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("后台翻译任务异常: " + ex.Message);
            }
            finally
            {
                // auto.xml：成功写译文（覆盖），失败写原文（不覆盖已有译文）。
                if (ok)
                {
                    TranslationPatch.WriteAutoKey(job.Key, translated, overwrite: true);
                }
                else
                {
                    TranslationPatch.WriteAutoKey(job.Key, job.Fallback, overwrite: false);
                }

                lock (PendingLock)
                {
                    Pending.Remove(job.Key);
                }
            }
        }
    }

    /// <summary>
    /// 将译文写进 RimWorld 当前激活语言的 keyedReplacements 字典，使其在下一次查询时实时命中。
    /// 不重载语言包、不动 xml 解析，开销仅一次字典赋值（加锁保护）。
    /// 若检测到语言已切换（folderName 与绑定值不符），则跳过，避免写错语言对象。
    /// </summary>
    private static void WriteToRimWorld(string key, string translated)
    {
        LoadedLanguage lang = LanguageDatabase.activeLanguage;
        if (lang == null)
        {
            return;
        }

        if (_boundFolderName != null && lang.folderName != _boundFolderName)
        {
            return; // 语言已切换，跳过回写
        }

        lock (DictLock)
        {
            lang.keyedReplacements[key] = new LoadedLanguage.KeyedReplacement
            {
                key = key,
                value = translated,
                isPlaceholder = false
            };
        }
    }
}
