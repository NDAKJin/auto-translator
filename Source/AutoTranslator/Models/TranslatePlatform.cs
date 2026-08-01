using System;
using System.Collections.Generic;

namespace nomadicooer.AutoTranslator;

/// <summary>
/// 翻译平台抽象基类。定义各翻译源必须实现的接口与共享的语言码解析辅助。
/// 节流与限流重试统一由 TranslationDispatcher（调度类）负责，本基类不再包含相关逻辑。
/// </summary>
public abstract class TranslatePlatform
{
    /// <summary>
    /// 平台标识键名（由调用方负责翻译为显示名，如 "AutoTranslator.Baidu"）。
    /// </summary>
    public abstract string PlatformName { get; }

    /// <summary>
    /// 将 RimWorld 语言名（如 "ChineseSimplified (简体中文)"）转为平台所需的语言码。
    /// 未知语言返回 null（派生类应据此返回 InvalidRequest）。
    /// </summary>
    /// <param name="name">RimWorld 语言名（含括号注释）。</param>
    /// <returns>平台语言码；不支持时返回 null。</returns>
    public abstract string GetLangCode(string name);

    /// <summary>
    /// 执行翻译。派生类应直接发起请求并返回结果，不含节流/重试逻辑。
    /// </summary>
    /// <param name="source">源语言（RimWorld 语言名，如 "English"）。</param>
    /// <param name="target">目标语言（RimWorld 语言名）。</param>
    /// <param name="text">待翻译原文。</param>
    /// <returns>翻译结果。</returns>
    public abstract TranslationResult Translate(string source, string target, string text);

    /// <summary>
    /// 从 "中文 (Chinese)" 或 "Chinese" 中提取括号前或纯英文部分，用于语言码查表。
    /// </summary>
    protected static string GetSimpleName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        int idx = name.IndexOf('(');
        return idx > 0 ? name.Substring(0, idx).Trim() : name.Trim();
    }
}
