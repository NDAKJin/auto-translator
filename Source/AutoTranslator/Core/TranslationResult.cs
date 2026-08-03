namespace nomadicooer.AutoTranslator;

/// <summary>
/// 单次翻译调用的聚合结果。
/// 把「译文 / 统一错误码 / 平台原始错误描述」封装在一起返回，
/// 取代原先分散在多个 out 参数上的 error / errorCode。
/// </summary>
public sealed class TranslationResult
{
    /// <summary>翻译后的文本；调用失败时通常为原文。</summary>
    public string TranslatedText { get; }

    /// <summary>统一错误码（跨平台一致，便于调用方按码分支或重试）。</summary>
    public TranslationErrorCode ErrorCode { get; }

    /// <summary>平台返回的原始错误描述（可读失败原因）。成功时为空字符串。</summary>
    public string RawError { get; }

    /// <summary>是否翻译成功（错误码为 <see cref="TranslationErrorCode.Success"/>）。</summary>
    public bool Success => ErrorCode == TranslationErrorCode.Success;

    private TranslationResult(string translatedText, TranslationErrorCode errorCode, string rawError)
    {
        TranslatedText = translatedText;
        ErrorCode = errorCode;
        RawError = rawError ?? string.Empty;
    }

    /// <summary>构造一个成功结果。</summary>
    public static TranslationResult Ok(string translatedText)
        => new TranslationResult(translatedText, TranslationErrorCode.Success, string.Empty);

    /// <summary>构造一个失败结果（携带统一错误码与平台原始错误描述）。</summary>
    public static TranslationResult Fail(
        string translatedText,
        TranslationErrorCode errorCode,
        string rawError)
        => new TranslationResult(translatedText, errorCode, rawError);
}
