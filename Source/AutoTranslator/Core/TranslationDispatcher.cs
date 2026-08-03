using System;
using System.Collections.Generic;
using System.Threading;

namespace nomadicooer.AutoTranslator;

/// <summary>
/// 翻译调度器：协调多个翻译源（阿里云/百度/火山等）。
/// 统一负责节流与限流重试，并根据各翻译源返回的 TranslationResult 错误码，
/// 对连续失败的源实施"关禁闭"（冷却）策略。
/// 各翻译源类只负责发起请求并返回结果，不含任何节流/重试逻辑。
/// 调度器自身实现 <see cref="ITranslationPlatform"/>（持有翻译源而非继承基类）。
/// </summary>
public class TranslationDispatcher : ITranslationPlatform
{
    private const string DispatcherNameKey = "AutoTranslator.Dispatcher";

    // ===== 可配置阈值（后续接入设置页；当前为字段默认值）=====
    /// <summary>连续失败次数超过该阈值后，将源关禁闭（进入冷却）。可由设置页修改。</summary>
    public int DisableThreshold = 3;

    /// <summary>关禁闭时长（毫秒）。冷却结束后自动恢复。可由设置页修改。</summary>
    public int CooldownMs = 60000;

    /// <summary>遇到限流（RateLimited）时对该源的最大重试次数。可由设置页修改。</summary>
    public int MaxRetryOnRateLimit = 3;

    /// <summary>限流退避基数（毫秒），第 n 次重试等待约 BaseBackoffMs * 2^(n-1)。可由设置页修改。</summary>
    public int BaseBackoffMs = 500;

    /// <summary>调度层对单个源的最小请求间隔（毫秒），避免频繁触发平台限流。可由设置页修改。</summary>
    public int MinIntervalMs = 200;

    /// <summary>
    /// 源选择策略：
    /// true = 随机（Random），每次从随机一个可用源开始尝试，平摊请求量、降低单源限流概率；
    /// false = 顺序（Sequential），始终从注册顺序的第一个可用源开始（原 fallback 行为）。
    /// 可由设置页修改。
    /// </summary>
    public bool UseRandom = true;

    /// <summary>随机起点生成器（Random 模式下挑选起始源）。</summary>
    private readonly Random _rng = new Random();

    private readonly List<ITranslationPlatform> _sources = new List<ITranslationPlatform>();

    // 每个翻译源的运行时状态：连续失败计数、关禁闭截止时间戳、上次请求时间戳。
    private readonly Dictionary<ITranslationPlatform, int> _failCounts = new Dictionary<ITranslationPlatform, int>();
    private readonly Dictionary<ITranslationPlatform, long> _cooldownUntil = new Dictionary<ITranslationPlatform, long>();
    private readonly Dictionary<ITranslationPlatform, long> _lastRequestTicks = new Dictionary<ITranslationPlatform, long>();
    private readonly object _stateLock = new object();

    /// <summary>
    /// 构造一个翻译调度器。翻译源由外部 new 好后通过构造参数或 <see cref="Register"/> 注册，
    /// 调度类自身不创建任何翻译源实例。
    /// </summary>
    /// <param name="sources">参与翻译的源，按优先级排列（前者先尝试）。可为空。</param>
    public TranslationDispatcher(IEnumerable<ITranslationPlatform> sources = null)
    {
        if (sources != null)
        {
            foreach (ITranslationPlatform s in sources)
            {
                Register(s);
            }
        }
    }

    /// <summary>
    /// 注册一个翻译源（外部需先 new 好实例）。注册到末尾，优先级低于已注册源。
    /// 重复注册同一实例会被忽略。
    /// </summary>
    /// <param name="source">已 new 好的翻译源实例。</param>
    public void Register(ITranslationPlatform source)
    {
        if (source == null)
        {
            return;
        }

        lock (_stateLock)
        {
            if (_sources.Contains(source))
            {
                return;
            }

            _sources.Add(source);
            _failCounts[source] = 0;
            _cooldownUntil[source] = 0;
            _lastRequestTicks[source] = 0;
        }
    }

    /// <summary>
    /// 调度器标识键名（由调用方负责翻译为显示名）。
    /// </summary>
    public string PlatformName => DispatcherNameKey;

    /// <summary>
    /// 调度器本身不翻译、也不维护语言映射，故直接返回 null。
    /// 语言解析由各翻译源内部完成；Translate 已将 RimWorld 语言名透传给各源。
    /// 外部不应调用本方法（仅为实现接口契约而存在）。
    /// </summary>
    public string GetLangCode(string name) => null;

    /// <summary>
    /// 按注册顺序协调各翻译源：跳过关禁闭中的源，对可用源先限流重试，
    /// 成功即返回；失败则累计计数，超过阈值则关禁闭该源并转下一个源。
    /// 全部失败返回最后一个源的结果。
    /// </summary>
    /// <param name="source">源语言（RimWorld 语言名，如 "English"）。</param>
    /// <param name="target">目标语言（RimWorld 语言名，如 "ChineseSimplified (简体中文)"）。</param>
    /// <param name="text">待翻译的原文。</param>
    /// <returns>首个成功源的译文，或全部失败时的末源错误结果。</returns>
    public TranslationResult Translate(string source, string target, string text)
    {
        if (_sources.Count == 0)
        {
            AutoLog.Error("[AutoTranslator] 翻译调度器未注册任何翻译源，无法执行翻译。请检查 Mod 设置中是否已启用并正确配置至少一个翻译源（如火山/阿里云/百度）的 API Key。");
            return TranslationResult.Fail(text, TranslationErrorCode.InvalidRequest, "翻译调度器无可用翻译源");
        }

        TranslationResult last = null;
        List<ITranslationPlatform> snapshot;
        int start;
        lock (_stateLock)
        {
            snapshot = new List<ITranslationPlatform>(_sources);
            // 随机模式：随机挑一个起点，平摊请求量、降低单源限流概率；顺序模式：始终从 0 开始。
            start = UseRandom ? _rng.Next(snapshot.Count) : 0;
        }

        // 从 start 起回绕遍历一圈，使本请求平摊到不同起点，避免单源限流。
        for (int i = 0; i < snapshot.Count; i++)
        {
            ITranslationPlatform platform = snapshot[(start + i) % snapshot.Count];

            if (IsInCooldown(platform))
            {
                AutoLog.Warn("[AutoTranslator] 翻译源 " + platform.PlatformName + " 处于关禁闭期，跳过。");
                continue;
            }

            EnsureMinInterval(platform);

            TranslationResult result = TryWithRateLimitRetry(platform, source, target, text);
            if (result.Success)
            {
                ResetFail(platform);
                return result;
            }

            last = result;
            RegisterFail(platform);
        }

        return last ?? TranslationResult.Fail(text, TranslationErrorCode.InvalidRequest, "所有翻译源均不可用");
    }

    /// <summary>
    /// 对单个源发起请求；若返回限流（RateLimited），按退避重试同一源至多 MaxRetryOnRateLimit 次。
    /// </summary>
    private TranslationResult TryWithRateLimitRetry(ITranslationPlatform platform, string source, string target, string text)
    {
        TranslationResult result = platform.Translate(source, target, text);
        int retry = 0;
        while (!result.Success && result.ErrorCode == TranslationErrorCode.RateLimited && retry < MaxRetryOnRateLimit)
        {
            retry++;
            int backoff = BaseBackoffMs * (1 << (retry - 1));
            AutoLog.Warn("[AutoTranslator] 翻译源 " + platform.PlatformName
                            + " 限流，第 " + retry + " 次重试，等待 " + backoff + "ms。");
            Thread.Sleep(backoff);
            result = platform.Translate(source, target, text);
        }

        return result;
    }

    /// <summary>
    /// 保证对同一源两次请求之间至少间隔 MinIntervalMs（节流），避免突发触发平台限流。
    /// </summary>
    private void EnsureMinInterval(ITranslationPlatform platform)
    {
        if (MinIntervalMs <= 0)
        {
            return;
        }

        long now = DateTime.UtcNow.Ticks;
        long last;
        lock (_stateLock)
        {
            _lastRequestTicks.TryGetValue(platform, out last);
            _lastRequestTicks[platform] = now;
        }

        long elapsedMs = (now - last) / TimeSpan.TicksPerMillisecond;
        if (elapsedMs < MinIntervalMs)
        {
            Thread.Sleep((int)(MinIntervalMs - elapsedMs));
        }
    }

    /// <summary>
    /// 登记一次失败：连续失败计数 +1，超过 DisableThreshold 则将该源关禁闭 CooldownMs。
    /// </summary>
    private void RegisterFail(ITranslationPlatform platform)
    {
        lock (_stateLock)
        {
            int count = 0;
            _failCounts.TryGetValue(platform, out count);
            count++;
            _failCounts[platform] = count;
            if (count > DisableThreshold)
            {
                _cooldownUntil[platform] = DateTime.UtcNow.Ticks + CooldownMs * TimeSpan.TicksPerMillisecond;
                AutoLog.Warn("[AutoTranslator] 翻译源 " + platform.PlatformName
                                + " 连续失败 " + count + " 次，关禁闭 " + CooldownMs + "ms。");
            }
        }
    }

    /// <summary>重置指定源的失败计数与关禁闭状态（成功翻译后调用）。</summary>
    private void ResetFail(ITranslationPlatform platform)
    {
        lock (_stateLock)
        {
            _failCounts[platform] = 0;
            _cooldownUntil[platform] = 0;
        }
    }

    /// <summary>判断指定源当前是否处于关禁闭（冷却）期。</summary>
    private bool IsInCooldown(ITranslationPlatform platform)
    {
        lock (_stateLock)
        {
            long until;
            return _cooldownUntil.TryGetValue(platform, out until) && until > DateTime.UtcNow.Ticks;
        }
    }
}
