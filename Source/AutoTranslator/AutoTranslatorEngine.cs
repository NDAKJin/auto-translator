using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Verse;

namespace nomadicooer.AutoTranslator;

/// <summary>
/// 自动翻译引擎（消费层，任务触发式异步）。
/// 主线程遇到未翻译 key 时调用 <see cref="RequestTranslate"/>，内部用 Task.Run 触发一次性后台任务，
/// 调用 <see cref="TranslationDispatcher"/>（其内部为同步阻塞实现）翻译，翻完即退出，不占用常驻线程。
/// 并发量由 <see cref="Throttle"/>（SemaphoreSlim）限制，避免瞬间大量 key 打爆 API 配额。
///
/// 为什么需要"回写内存"（<see cref="WriteToRimWorld"/>）：
///   Translator.TryTranslate 命中 keyedReplacements 即返回 true，postfix 不再进入。
///   回写后下一帧即显示译文（本局生效）；auto.xml 则负责持久化（下次启动免重复调 API）。
///   二者职责不同，缺一不可。
///
/// 两种模式（由设置"启用实时翻译"决定）：
///   实时翻译开启：调 API 翻译 -> 1) 回写 keyedReplacements（投递主线程执行，下一帧命中）；
///                                 2) 存内存 cache（已翻译 key 不再请求 API）；
///                                 3) 写 auto.xml（value=译文）。
///   实时翻译关闭：TranslationPatch 直接写 auto.xml，根本不进入本引擎。
///
/// auto.xml 的 value 含义：translated=false 为未翻译（value=英文原文），true 为已翻译（value=译文）。
/// "一键翻译"据此判断是否需要调 API。
/// 已翻译的 key 通过 cache + pending 双层去重，绝不重复打 API。
/// 切换语言概率极低，未做完整兼容：回写时检测到 activeLanguage 已变则跳过（不崩，仅该批译文不生效）。
/// </summary>
public static class AutoTranslatorEngine
{
    /// <summary>
    /// 后台翻译任务的最大并发数。RimWorld 启动/进入界面时可能瞬间涌入大量未翻译 key，
    /// 不限流会同时发起成百上千个 HTTP 请求，既打爆第三方 API 配额也拖垮线程池。
    /// </summary>
    private const int MaxConcurrency = 4;

    private static readonly SemaphoreSlim Throttle = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);

    // 已翻译结果缓存：原文 key -> 译文。后台任务写、主线程（经 RequestTranslate 去重）读。
    private static readonly ConcurrentDictionary<string, string> Cache =
        new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

    // 在途去重：译文回来前同一 key 只提交一次。ConcurrentDictionary 当 Set 用（TryAdd 原子）。
    private static readonly ConcurrentDictionary<string, byte> Pending =
        new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

    private static TranslationDispatcher _dispatcher;
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

            // 按 cred.PlatformId 匹配引擎类的 PlatformNameKey，自动实例化，无写死 switch。
            TranslatePlatform source = CreatePlatform(cred.PlatformId, cred.AccessKey, cred.SecretKey);
            if (source != null)
            {
                sources.Add(source);
            }
        }

        return sources;
    }

    /// <summary>
    /// 启用自动翻译引擎并注入翻译源。应在主线程初始化阶段调用一次
    /// （如 <see cref="AutoTranslatorStartup"/> 在总开关 + 实时翻译均开启时触发）。
    /// 启用条件（总开关 / 实时翻译）由调用方判断，本方法只负责构建翻译源并置为就绪。
    /// 不再创建常驻线程：翻译任务在 <see cref="RequestTranslate"/> 时按需用 Task.Run 触发，跑完即退。
    /// 引擎启用不依赖"是否配置了平台凭据"：未配凭据时也就绪（dispatcher 带空源集合，
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

            ApplySources(BuildSources(), "自动翻译引擎已启用");
        }
    }

    /// <summary>
    /// 运行期刷新翻译源（玩家在设置页补填 / 修改凭据并保存时调用）。
    /// 重新从当前设置构建 dispatcher 并替换引擎持有的引用；若引擎此前因总开关/实时翻译关闭而未启用，
    /// 则在此一并启用；若总开关或实时翻译已关闭，则停用引擎。
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

            ApplySources(BuildSources(), "翻译源已刷新");
        }
    }

    /// <summary>
    /// 停用引擎并清空翻译源（总开关关闭 / 实时翻译关闭时调用）。
    /// 无常驻线程需要终止，仅置标志位：在途任务跑完自然结束，新请求会被 _running 挡下。
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
            Logger.Message("自动翻译引擎已停用。");
        }
    }

    /// <summary>
    /// 应用翻译源并置引擎为就绪（假定 EngineLock 已持有）。首次启用与运行期刷新共用。
    /// </summary>
    private static void ApplySources(List<TranslatePlatform> sources, string logPrefix)
    {
        _dispatcher = new TranslationDispatcher(sources);
        _boundFolderName = LanguageDatabase.activeLanguage?.folderName;
        _running = true;

        if (sources.Count == 0)
        {
            Logger.Warning(logPrefix + "，但未配置任何翻译平台凭据，翻译不会生效（需在设置页配置凭据）。");
        }
        else
        {
            Logger.Message(string.Format("{0}，已启用 {1} 个翻译平台。", logPrefix, sources.Count));
        }
    }

    /// <summary>
    /// 提交一条翻译请求（实时翻译开启时使用）。立即返回，不阻塞主线程。
    /// 已翻译（命中 cache）或在途（命中 pending）的 key 会被去重，不重复打 API。
    /// 引擎未启用（未 Configure）时直接忽略。
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

        Submit(key, source, target, fallback);
    }

    /// <summary>
    /// 一键翻译：读取 auto.xml，仅筛选 translated=false 的待翻译条目，
    /// 批量提交翻译任务（并发受 <see cref="Throttle"/> 限制），翻译成功写 auto.xml（translated=true）。
    /// 不再依赖"value 是否等于原文"推断，彻底避免引擎返回原文导致的死循环。
    /// 引擎未启用或无需翻译时直接返回。
    /// </summary>
    public static void TranslateAllPending()
    {
        if (!_running)
        {
            Logger.Warning("自动翻译引擎未启用，无法执行一键翻译。");
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
        foreach (KeyValuePair<string, AutoXmlWriter.AutoKeyEntry> kv in autoKeys)
        {
            // 仅提交未翻译（translated=false）的条目，已翻译的跳过。
            if (kv.Value.Translated)
            {
                continue;
            }

            string key = kv.Key;
            string fallback = kv.Value.Value ?? string.Empty;
            if (string.IsNullOrEmpty(fallback))
            {
                // 兜底：记录值为空时取默认语言原文，仍为空则用 key 本身。
                TaggedString defaultText;
                LanguageDatabase.defaultLanguage.TryGetTextFromKey(key, out defaultText);
                fallback = defaultText.NullOrEmpty() ? key : defaultText.ToString();
            }

            if (Submit(key, source, target, fallback))
            {
                submitted++;
            }
        }

        if (submitted == 0)
        {
            Logger.Message("auto.xml 中没有需要翻译的 key。");
        }
        else
        {
            Logger.Message(string.Format("已提交 {0} 个待翻译 key。", submitted));
        }
    }

    /// <summary>
    /// 在途去重后用 Task.Run 触发一次性后台翻译任务。返回是否实际提交。
    /// 任务内先等信号量（限流），拿到名额才真正打 API；跑完释放名额并移除 pending，任务自然结束。
    /// </summary>
    private static bool Submit(string key, string source, string target, string fallback)
    {
        // TryAdd 原子操作：已在途则返回 false，不重复提交。
        if (!Pending.TryAdd(key, 0))
        {
            return false;
        }

        Task.Run(() => RunTranslateTask(key, source, target, fallback));
        return true;
    }

    /// <summary>
    /// 单个 key 的后台翻译流程：限流 -> 调 API -> 回写内存（投递主线程） -> 写 auto.xml -> 释放。
    /// 全程 try/finally 保证信号量与 pending 一定被释放，不会因异常泄漏名额导致引擎卡死。
    /// </summary>
    private static void RunTranslateTask(string key, string source, string target, string fallback)
    {
        bool ok = false;
        string translated = null;

        try
        {
            Throttle.Wait();
            try
            {
                // 本地捕获，避免 RefreshSources 并发替换引用导致空引用。
                TranslationDispatcher dispatcher = _dispatcher;
                if (!_running || dispatcher == null)
                {
                    return;
                }

                TranslationResult result = dispatcher.Translate(source, target, fallback);
                if (result.Success && !string.IsNullOrEmpty(result.TranslatedText))
                {
                    // 译文落盘/回写前做占位符规范化与校验：
                    // 机翻常把 {0} 翻成全角 ｛0｝（游戏不会解析，原样显示），或漏/多占位符导致运行时格式化异常。
                    // 不通过则丢弃译文，按失败处理（写原文，不污染 auto.xml，下次仍可重翻）。
                    string sanitized = SanitizeTranslation(fallback, result.TranslatedText);
                    if (sanitized != null)
                    {
                        ok = true;
                        translated = sanitized;
                        Cache[key] = translated;
                        WriteToRimWorld(key, translated);
                    }
                }
            }
            finally
            {
                Throttle.Release();
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("后台翻译任务异常: " + ex.Message);
        }
        finally
        {
            // auto.xml：成功写译文（translated=true，覆盖），失败写原文（translated=false，不覆盖已有译文）。
            try
            {
                if (ok)
                {
                    AutoXmlWriter.WriteAutoKey(key, translated, translated: true, overwrite: true);
                }
                else
                {
                    AutoXmlWriter.WriteAutoKey(key, fallback, translated: false, overwrite: false);
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("写入 auto.xml 失败: " + ex.Message);
            }

            byte ignored;
            Pending.TryRemove(key, out ignored);
        }
    }

    /// <summary>
    /// 译文落盘/回写前的占位符规范化与校验。
    /// 翻译 API（尤其机翻）常把 RimWorld 的半角占位符 {0}/{1} 翻成全角 ｛0｝（游戏不解析，原样显示），
    /// 或漏翻/多翻占位符导致运行期 string.Format 抛异常。
    /// 处理：1) 全角花括号 ｛｝ 还原为半角 {}；2) 提取译文与原文的 {N} 数字集合比对，不一致视为损坏。
    /// 返回规范化后的译文；损坏（占位符集合不匹配）返回 null，调用方应丢弃译文按失败处理。
    /// </summary>
    private static string SanitizeTranslation(string source, string translated)
    {
        if (string.IsNullOrEmpty(translated))
        {
            return null;
        }

        // 1) 全角花括号还原为半角（机翻最常见的占位符损坏形式）。
        string normalized = translated.Replace('｛', '{').Replace('｝', '}');

        // 2) 提取原文与译文的 {N} 占位符数字集合并比对。
        HashSet<int> sourceArgs = ExtractPlaceholderIndices(source);
        HashSet<int> translatedArgs = ExtractPlaceholderIndices(normalized);
        if (!sourceArgs.SetEquals(translatedArgs))
        {
            Logger.Warning(string.Format(
                "译文占位符不匹配，已丢弃（原文 {0} 个 / 译文 {1} 个）：{2}",
                sourceArgs.Count, translatedArgs.Count, source));
            return null;
        }

        return normalized == translated ? translated : normalized;
    }

    /// <summary>
    /// 提取字符串中 {N} 形式的占位符数字（N 为非负整数）。RimWorld 文本格式化使用 {0}/{1}/...。
    /// 用正则匹配避免误伤普通花括号文本。
    /// </summary>
    private static HashSet<int> ExtractPlaceholderIndices(string text)
    {
        var set = new HashSet<int>();
        if (string.IsNullOrEmpty(text))
        {
            return set;
        }

        // 匹配 { 数字 }（允许数字前后有空白，兼容 {0} 与 { 0 } 写法）。
        System.Text.RegularExpressions.MatchCollection matches =
            System.Text.RegularExpressions.Regex.Matches(text, @"\{\s*(\d+)\s*\}");
        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            int idx;
            if (int.TryParse(m.Groups[1].Value, out idx))
            {
                set.Add(idx);
            }
        }

        return set;
    }

    /// <summary>
    /// 将译文写进 RimWorld 当前激活语言的 keyedReplacements 字典，使其在下一次查询时实时命中。
    /// 不重载语言包、不动 xml 解析，开销仅一次字典赋值。
    ///
    /// 线程安全：keyedReplacements 是普通 Dictionary，RimWorld 主线程每帧都在读；
    /// 后台线程直接写可能在扩容瞬间与读并发导致崩溃。因此通过 LongEventHandler.ExecuteWhenFinished
    /// 把写操作投递回主线程执行（下一帧生效，与"下一帧才重绘"的时序天然一致），彻底免锁。
    /// 若届时语言已切换（folderName 与绑定值不符），则跳过，避免写错语言对象。
    /// </summary>
    private static void WriteToRimWorld(string key, string translated)
    {
        LongEventHandler.ExecuteWhenFinished(() =>
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

            lang.keyedReplacements[key] = new LoadedLanguage.KeyedReplacement
            {
                key = key,
                value = translated,
                isPlaceholder = false
            };
        });
    }
}
