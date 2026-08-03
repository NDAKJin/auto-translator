namespace nomadicooer.AutoTranslator;

/// <summary>
/// 翻译平台统一契约：平台标识、语言码映射、翻译。
/// 抽象基类 <see cref="TranslatePlatform"/> 实现本接口并承载通用校验（模板方法）；
/// <see cref="TranslationDispatcher"/> 也实现本接口以作为可派发的翻译源，但不继承基类。
/// </summary>
public interface ITranslationPlatform
{
    /// <summary>平台标识（带前缀的完整标志，如 "AutoTranslator.Volcengine"）。</summary>
    string PlatformName { get; }

    /// <summary>
    /// RimWorld 语言文件夹的英文名（如 "ChineseSimplified (简体中文)"）→ 平台翻译语言码。
    /// 无法映射时返回 null（调用方据此判 InvalidRequest）。
    /// </summary>
    string GetLangCode(string rimWorldLangFolderName);

    /// <summary>
    /// 翻译：source/target 为 RimWorld 语言文件夹名（由内部先解析为平台码），
    /// text 为待译原文。失败返回 <see cref="TranslationResult"/>（Success=false）。
    /// </summary>
    TranslationResult Translate(string source, string target, string text);
}
