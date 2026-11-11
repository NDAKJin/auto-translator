namespace nomadicooer.AutoTranslator;

/// <summary>
/// 翻译接口统一使用的错误码。
/// 由各自实现映射到此统一枚举，调用方无需关心具体平台即可按码分支。
/// </summary>
public enum TranslationErrorCode
{
    /// <summary>翻译成功。</summary>
    Success = 0,

    /// <summary>
    /// 临时限流 / 可重试的服务端繁忙（如 QPS 超限、请求超时、系统繁忙）。
    /// 调用方可在退避后重试。
    /// </summary>
    RateLimited = 1,

    /// <summary>
    /// 服务不可用 / 网络异常 / 响应解析失败等需要排查的故障。
    /// 默认不可重试（或需人工介入）。
    /// </summary>
    ServiceUnavailable = 2,

    /// <summary>
    /// 请求/参数类错误（含凭证缺失、空文本、不支持的语种、签名错误、余额不足等）。
    /// 属客户端可修正错误，不应重试。
    /// </summary>
    InvalidRequest = 3,

    // 可扩展：如 QuotaExhausted = 4 等，按需补充。
}
