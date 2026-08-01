using System;
using System.Diagnostics;
using System.Threading;

namespace nomadicooer.AutoTranslator;

/// <summary>
/// RimWorld 语言包名称解析辅助。
/// RimWorld 语言文件夹名为 "&lt;SimpleName&gt; (&lt;LocalName&gt;)" 形式，
/// 例如 "ChineseSimplified (简体中文)"、"SpanishLatin (Español(Latinoamérica))"。
/// 默认语言 English 不含括号。
/// </summary>
public abstract class TranslatePlatform
{
    /// <summary>
    /// 两次请求之间的最小间隔（毫秒）。子类可覆盖以匹配各自套餐的 QPS 上限。
    /// 默认 100ms，即约 10 QPS。免费版通常仅 1 QPS，应覆盖为 1000。
    /// </summary>
    protected virtual int MinIntervalMs => 100;

    /// <summary>
    /// 撞到限流（QPS 超限）时的最大重试次数。设为 0 表示不重试。
    /// </summary>
    protected virtual int MaxRetryOnRateLimit => 3;

    /// <summary>
    /// 限流重试的基础退避时长（毫秒）。第 n 次重试等待 BaseBackoffMs * 2^(n-1)。
    /// </summary>
    protected virtual int BaseBackoffMs => 500;

    /// <summary>上次请求发出的时间戳，用于节流。按实例隔离。</summary>
    private long _lastRequestTicks;

    /// <summary>节流与重试的同步锁，保证多线程调用时串行化请求。</summary>
    private readonly object _throttleLock = new object();

    /// <summary>
    /// 判断给定的统一错误码是否属于「限流/QPS 超限」类错误（可通过等待后重试恢复）。
    /// 子类按各自映射结果覆盖；默认不认为任何错误可重试。
    /// </summary>
    /// <param name="errorCode">映射后的统一错误码。</param>
    protected virtual bool IsRateLimitError(TranslationErrorCode errorCode)
        => errorCode == TranslationErrorCode.RateLimited;

    /// <summary>
    /// 阻塞当前线程直到距上次请求已满 <see cref="MinIntervalMs"/>，用于主动规避 QPS 超限。
    /// </summary>
    private void Throttle()
    {
        int interval = MinIntervalMs;
        if (interval <= 0)
        {
            return;
        }

        long now = DateTime.UtcNow.Ticks;
        long elapsedMs = (now - _lastRequestTicks) / TimeSpan.TicksPerMillisecond;
        if (_lastRequestTicks != 0 && elapsedMs < interval)
        {
            Thread.Sleep((int)(interval - elapsedMs));
        }

        _lastRequestTicks = DateTime.UtcNow.Ticks;
    }

    /// <summary>
    /// 在节流与限流重试的保护下执行一次翻译请求。
    /// 子类的 Translate 应把实际请求逻辑包在此方法中，即可自动获得 QPS 控制能力。
    /// 请求委托返回 <see cref="TranslationResult"/>，其中已携带统一错误码与原始错误描述。
    /// </summary>
    /// <param name="request">实际发起请求的委托，返回本次请求的聚合结果。</param>
    /// <returns>本次翻译的聚合结果（含译文、统一错误码、原始错误描述）。</returns>
    protected TranslationResult ExecuteWithThrottle(RequestFunc request)
    {
        lock (_throttleLock)
        {
            TranslationResult last = null;

            for (int attempt = 0; attempt <= MaxRetryOnRateLimit; attempt++)
            {
                Throttle();
                last = request();

                // 成功：直接返回
                if (last.Success)
                {
                    return last;
                }

                // 非限流类错误：不重试
                if (!IsRateLimitError(last.ErrorCode))
                {
                    return last;
                }

                // 已是最后一次尝试，不再等待
                if (attempt == MaxRetryOnRateLimit)
                {
                    break;
                }

                // 指数退避后重试
                int backoff = BaseBackoffMs * (1 << attempt);
                Debug.WriteLine("[AutoTranslator] 触发限流(" + last.ErrorCode + ")，" + backoff
                                + "ms 后重试（第 " + (attempt + 1) + "/" + MaxRetryOnRateLimit + " 次）");
                Thread.Sleep(backoff);
            }

            // 限流重试耗尽：在原始错误描述上追加重试次数信息
            return TranslationResult.Fail(
                last.TranslatedText,
                last.ErrorCode,
                last.RawError + "（已重试 " + MaxRetryOnRateLimit + " 次仍失败）");
        }
    }

    /// <summary>
    /// 翻译请求委托：返回本次请求的聚合结果（含译文、统一错误码、原始错误描述）。
    /// </summary>
    protected delegate TranslationResult RequestFunc();

    /// <summary>
    /// 获取 RimWorld 语言包的简单名称（去除括号及之后的本地名部分）。
    /// 例如 "ChineseSimplified (简体中文)" → "ChineseSimplified"；
    /// 若传入已是简单名称（不含 " ("）则原样返回。
    /// </summary>
    public static string GetSimpleName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        int idx = name.IndexOf(" (");
        return idx >= 0 ? name.Substring(0, idx) : name;
    }
    /// <summary>
    /// 将 RimWorld 语言的简单名称（如 "ChineseSimplified"）转换为对应翻译平台的语言代码。
    /// 未知语言返回 null。
    /// </summary>
    /// <param name="name">RimWorld 语言简单名称（即 <see cref="GetSimpleName"/> 的返回值）。</param>
    /// <returns>平台语言代码（如 "zh"）；无法识别时返回 null。</returns>
    public abstract string GetLangCode(string name);

    /// <summary>
    /// 平台名称（经过翻译的显示名），子类必须提供。
    /// 通常用英文平台名作键，例如 <c>AutoTranslator.Volcengine</c>。
    /// </summary>
    public abstract string PlatformName { get; }

    /// <summary>
    /// 调用具体翻译平台，将文本从源语言翻译到目标语言。
    /// </summary>
    /// <param name="source">源语言代码（由 <see cref="GetLangCode"/> 从 RimWorld 语言解析得到，如 "en"）。</param>
    /// <param name="target">目标语言代码（如 "zh"）。</param>
    /// <param name="text">待翻译的原文。</param>
    /// <returns>本次翻译的聚合结果，含译文、统一错误码与平台原始错误描述。</returns>
    public abstract TranslationResult Translate(string source, string target, string text);
}
