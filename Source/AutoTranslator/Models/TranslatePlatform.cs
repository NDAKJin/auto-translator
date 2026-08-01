namespace nomadicooer.AutoTranslator.Models;

/// <summary>
/// RimWorld 语言包名称解析辅助。
/// RimWorld 语言文件夹名为 "&lt;SimpleName&gt; (&lt;LocalName&gt;)" 形式，
/// 例如 "ChineseSimplified (简体中文)"、"SpanishLatin (Español(Latinoamérica))"。
/// 默认语言 English 不含括号。
/// </summary>
public abstract class TranslatePlatform
{
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
    /// <returns>翻译后的文本；调用失败时返回 null 或原文。</returns>
    public abstract string Translate(string source, string target, string text);
}
