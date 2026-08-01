using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Verse;

namespace nomadicooer.AutoTranslator;

/// <summary>
/// 自动翻译引擎（消费层，异步）。
/// 主线程遇到未翻译 key 时调用 <see cref="RequestTranslate"/> 入队（不阻塞）；
/// 后台线程从队列取任务，调用 <see cref="TranslationDispatcher"/>（其内部为同步阻塞实现）翻译。
///
/// 两种模式（由设置"启用实时翻译"决定）：
///   实时翻译开启：调 API 翻译 -> 1) 写 RimWorld 当前语言 keyedReplacements（实时回写内存，下次查询即命中）；
///                                 2) 存内存 cache（已翻译 key 不再请求 API）；
///                                 3) 写 auto.xml（value=译文）。
///   实时翻译关闭：不调 API、不回写内存，仅把 key 与默认语言内容写入 auto.xml（value=英文原文），
///                 供后续"一键翻译"批量消费。
///
/// auto.xml 的 value 含义：等于默认语言内容 = 未翻译；已翻译 = 译文。
/// "一键翻译"据此判断是否需要调 API。
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
        public string Fallback; // 默认语言（英文）原文内容，用于记录/回退
    }

    // 待翻译队列：后台线程从此处取任务。BlockingCollection 在 .NET Framework 4.0+ 可用。
    // 非 readonly：Stop 后需要重建新队列供重新启动时消费（CompleteAdding 后的集合不可再用）。
    private static BlockingCollection<Job> Queue = new BlockingCollection<Job>();

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
    private static readonly object EngineLock = new object();

    // 绑定回写的语言 folderName；切语言后据此跳过回写，避免写错语言对象。
    private static string _boundFolderName;

    /// <summary>
    /// 平台标志（完整 PlatformNameKey，如 "AutoTranslator.Volcengine"） → 对应 TranslatePlatform 子类的缓存。
    /// 启动时反射扫描一次，之后按 cred.PlatformId（即 PlatformNameKey）直接取类型实例化，
    /// 不再写死的 switch，新增/删除平台只改对应引擎类本身。
    /// 原位于 AutoTranslatorStartup，下沉到 Engine 以支持运行期凭据刷新（避免分层反向依赖）。
    /// </summary>
    private static readonly Dictionary<string, Type> PlatformTypes = ScanPlatformTypes();

    /// <summary>
    /// 反射扫描本程序集内所有 TranslatePlatform 子类，读取其公共常量 PlatformNameKey
    /// （"AutoTranslator.Volcengine" 形式）作为平台标志建索引。
    /// </summary>
    private static Dictionary<string, Type> ScanPlatformTypes()
    {
        var map = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (Type type in Assembly.GetExecutingAssembly().GetTypes())
        {
            if (type.IsAbstract || type.IsInterface || !typeof(TranslatePlatform).IsAssignableFrom(type))
            {
                continue;
            }

            // 找公共常量 PlatformNameKey（引擎类已改为 public）
            FieldInfo field = type.GetField("PlatformNameKey", BindingFlags.Public | BindingFlags.Static);
            if (field == null || !field.IsLiteral || field.FieldType != typeof(string))
            {
                continue;
            }

            string fullKey = (string)field.GetValue(null);
            if (!string.IsNullOrEmpty(fullKey))
            {
                map[fullKey] = type;
            }
        }

        return map;
    }

    /// <summary>
    /// 按平台标志（cred.PlatformId，应为完整 PlatformNameKey）实例化翻译平台
    /// （构造函数签名为 (string accessKey, string secretKey)）。
    /// 优先用完整 PlatformNameKey 匹配；找不到时兼容旧存档里的裸短名（去前缀形式）。
    /// 找不到对应类型返回 null。
    /// </summary>
    private static TranslatePlatform CreatePlatform(string platformId, string accessKey, string secretKey)
    {
        string resolved = platformId;
        if (!PlatformTypes.TryGetValue(resolved, out Type type))
        {
            // 兼容旧存档：cred.PlatformId 可能是不带前缀的裸短名（如 "Volcengine"）
            string prefixed = AutoTranslatorConstants.TranslationKeyPrefix + platformId;
            if (PlatformTypes.TryGetValue(prefixed, out type))
            {
                resolved = prefixed;
            }
            else
            {
                Logger.Warning("未知翻译平台: " + platformId);
                return null;
            }
        }

        try
        {
            return (TranslatePlatform)Activator.CreateInstance(type, accessKey, secretKey);
        }
        catch (Exception ex)
        {
            Logger.Warning("创建翻译平台实例失败: " + resolved + " -> " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// 从当前设置（AutoTranslatorMod.Settings.Credentials）构建翻译源列表。
    /// 仅收录 accessKey / secretKey 均非空的凭据；凭据不完整则跳过。
    /// </summary>
    private static List<TranslatePlatform> BuildSources()
    {
        var sources = new List<TranslatePlatform>();
        AutoTranslatorSettings settings = AutoTranslatorMod.Settings;
        if (settings == null)
        {
            return sources;
        }

        foreach (PlatformCredential cred in settings.Credentials)
        {
            if (string.IsNullOrEmpty(cred.AccessKey) || string.IsNullOrEmpty(cred.SecretKey))
            {
                continue; // 凭据不完整则跳过
            }

            // 按 cred.PlatformId 匹配引擎类的 PlatformNameKey（去前缀），自动实例化，无写死 switch。
            TranslatePlatform source = CreatePlatform(cred.PlatformId, cred.AccessKey, cred.SecretKey);
            if (source != null)
            {
                sources.Add(source);
            }
        }

        return sources;
    }

    /// <summary>
    /// 启动自动翻译引擎并注入翻译源。应在主线程初始化阶段调用一次
    /// （如 <see cref="AutoTranslatorStartup"/> 在总开关 + 实时翻译均开启时触发）。
    /// 启动条件（总开关 / 实时翻译）由调用方判断，本方法只负责构建翻译源 + 启动后台消费线程。
    /// 引擎启动不依赖"是否配置了平台凭据"：未配凭据时也启动（dispatcher 带空源集合，
    /// 翻译请求会因 _sources.Count==0 安全返回失败原文，不会崩溃）。
    /// 玩家运行期在设置页补填凭据后由 <see cref="RefreshSources"/> 即时生效，无需重启游戏。
    /// </summary>
    public static void Configure()
    {
        lock (EngineLock)
        {
            if (_running)
            {
                return;
            }

            List<TranslatePlatform> sources = BuildSources();
            StartEngine(sources);
        }
    }

    /// <summary>
    /// 运行期刷新翻译源（玩家在设置页补填 / 修改凭据并保存时调用）。
    /// 重新从当前设置构建 dispatcher 并替换引擎持有的引用；若引擎此前因总开关/实时翻译关闭而未启动，
    /// 则在此一并启动；若总开关或实时翻译已关闭，则停止引擎。
    /// 这样首次启动未填凭据的玩家，在设置页填完密钥保存后即可即时生效，无需重启游戏。
    /// </summary>
    public static void RefreshSources()
    {
        lock (EngineLock)
        {
            if (!AutoTranslatorMod.AutoTranslateEnabled || !AutoTranslatorMod.RealtimeUpdateEnabled)
            {
                Stop();
                return;
            }

            List<TranslatePlatform> sources = BuildSources();
            _dispatcher = new TranslationDispatcher(sources);
            if (!_running)
            {
                StartWorker();
            }

            if (sources.Count == 0)
            {
                Logger.Warning("未配置任何翻译平台凭据，自动翻译引擎已启动但翻译不会生效（需在设置页配置凭据）。");
            }
            else
            {
                Logger.Message(string.Format("翻译源已刷新，已启用 {0} 个翻译平台。", sources.Count));
            }
        }
    }

    /// <summary>
    /// 停止后台消费线程并清空翻译源（总开关关闭 / 实时翻译关闭时调用）。
    /// </summary>
    public static void Stop()
    {
        lock (EngineLock)
        {
            if (!_running)
            {
                return;
            }

            _running = false;
            _dispatcher = null;
            Queue.CompleteAdding();
            Logger.Message("自动翻译引擎已停止。");
        }
    }

    /// <summary>
    /// 构建 dispatcher 并启动后台线程（假定 EngineLock 已持有）。首次启动或刷新后补全启动用。
    /// </summary>
    private static void StartEngine(List<TranslatePlatform> sources)
    {
        _dispatcher = new TranslationDispatcher(sources);
        StartWorker();

        if (sources.Count == 0)
        {
            Logger.Warning("未配置任何翻译平台凭据，自动翻译引擎已启动但翻译不会生效（需在设置页配置凭据）。");
        }
        else
        {
            Logger.Message(string.Format("自动翻译引擎已启动（后台消费线程），已启用 {0} 个翻译平台。", sources.Count));
        }
    }

    /// <summary>
    /// 启动后台消费线程（假定 EngineLock 已持有且 _dispatcher 已就绪）。
    /// 旧的 BlockingCollection 若已 CompleteAdding，则重建一个新队列供新线程消费。
    /// </summary>
    private static void StartWorker()
    {
        _boundFolderName = LanguageDatabase.activeLanguage?.folderName;
        if (Queue.IsAddingCompleted)
        {
            Queue = new BlockingCollection<Job>();
        }

        _running = true;
        _worker = new Thread(ConsumeLoop)
        {
            IsBackground = true,
            Name = "AutoTranslator.Worker"
        };
        _worker.Start();
    }

    /// <summary>
    /// 提交一条翻译请求（实时翻译开启时使用）。立即返回，不阻塞主线程。
    /// 已翻译（命中 cache）或在途（命中 pending）的 key 会被去重，不重复打 API。
    /// 引擎未启动（未 Configure）时直接忽略。
    /// </summary>
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

        Enqueue(key, source, target, fallback);
    }

    /// <summary>
    /// 一键翻译：读取 auto.xml，仅筛选 translated=false 的待翻译条目，
    /// 批量提交翻译请求（强制覆盖在途去重），翻译成功写 auto.xml（translated=true）。
    /// 不再依赖"value 是否等于原文"推断，彻底避免引擎返回原文导致的死循环。
    /// 引擎未启动或无需翻译时直接返回。
    /// </summary>
    public static void TranslateAllPending()
    {
        if (!_running)
        {
            Logger.Warning("自动翻译引擎未启动，无法执行一键翻译。");
            return;
        }

        string source = LanguageDatabase.defaultLanguage?.folderName;
        string target = LanguageDatabase.activeLanguage?.folderName;
        if (source == null || target == null)
        {
            return;
        }

        Dictionary<string, AutoXmlWriter.AutoKeyEntry> autoKeys = AutoXmlWriter.LoadAutoKeys();
        int submitted = 0;
        foreach (var kv in autoKeys)
        {
            string key = kv.Key;
            AutoXmlWriter.AutoKeyEntry entry = kv.Value;

            // 仅提交未翻译（translated=false）的条目，已翻译的跳过。
            if (entry.Translated)
            {
                continue;
            }

            string fallback = entry.Value ?? string.Empty;
            if (string.IsNullOrEmpty(fallback))
            {
                // 兜底：记录值为空时用 key 本身作为原文。
                TaggedString defaultText;
                LanguageDatabase.defaultLanguage.TryGetTextFromKey(key, out defaultText);
                fallback = defaultText.NullOrEmpty() ? key : defaultText.ToString();
            }

            // 强制入队：绕过在途去重（这些 key 尚未翻译完成）。
            lock (PendingLock)
            {
                Pending.Add(key);
            }

            if (Queue.IsAddingCompleted)
            {
                Logger.Warning("自动翻译引擎已停止，无法继续提交一键翻译任务。");
                break;
            }

            Queue.Add(new Job
            {
                Key = key,
                Source = source,
                Target = target,
                Fallback = fallback
            });
            submitted++;
        }

        if (submitted == 0)
        {
            Logger.Message("auto.xml 中没有需要翻译的 key。");
        }
        else
        {
            Logger.Message(string.Format("已提交 {0} 个待翻译 key 至一键翻译队列。", submitted));
        }
    }

    private static void Enqueue(string key, string source, string target, string fallback)
    {
        lock (PendingLock)
        {
            if (Pending.Contains(key))
            {
                return; // 已在途中，不重复提交
            }

            Pending.Add(key);
        }

        if (Queue.IsAddingCompleted)
        {
            return; // 引擎已停止（队列已 CloseAdding），丢弃该请求
        }

        Queue.Add(new Job
        {
            Key = key,
            Source = source,
            Target = target,
            Fallback = fallback
        });
    }

    private static void ConsumeLoop()
    {
        foreach (Job job in Queue.GetConsumingEnumerable())
        {
            bool ok = false;
            string translated = null;
            TranslationDispatcher dispatcher = _dispatcher; // 本地捕获，避免 RefreshSources 替换后并发读
            try
            {
                if (AutoTranslatorMod.RealtimeUpdateEnabled && dispatcher != null)
                {
                    // 实时翻译开启：调 API 翻译 -> 回写内存 -> 写 auto.xml（译文）
                    TranslationResult result = dispatcher.Translate(job.Source, job.Target, job.Fallback);
                    if (result.Success && !string.IsNullOrEmpty(result.TranslatedText))
                    {
                        ok = true;
                        translated = result.TranslatedText;
                        Cache[job.Key] = translated;
                        WriteToRimWorld(job.Key, translated);
                    }
                }
                else
                {
                    // 实时翻译关闭：仅把 key 与默认语言内容写入 auto.xml，不调 API、不回写内存。
                    AutoXmlWriter.WriteAutoKey(job.Key, job.Fallback, translated: false, overwrite: false);
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("后台翻译任务异常: " + ex.Message);
            }
            finally
            {
                if (AutoTranslatorMod.RealtimeUpdateEnabled)
                {
                    // auto.xml：成功写译文（translated=true，覆盖），失败写原文（translated=false，不覆盖已有译文）。
                    if (ok)
                    {
                        AutoXmlWriter.WriteAutoKey(job.Key, translated, translated: true, overwrite: true);
                    }
                    else
                    {
                        AutoXmlWriter.WriteAutoKey(job.Key, job.Fallback, translated: false, overwrite: false);
                    }
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
