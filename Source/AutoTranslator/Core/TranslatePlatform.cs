using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

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

    /// <summary>
    /// 从扁平 JSON 文本中提取某字段的值（不依赖外部 JSON 库）。
    /// 同时支持字符串字面量（"key":"value"，处理转义）与数字字面量（"key":100）。
    /// 找不到时返回 null。
    /// </summary>
    protected static string ExtractField(string json, string key)
    {
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return null;

        // 字符串形式："key":"value"
        Match str = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
        if (str.Success) return UnescapeJson(str.Groups[1].Value);

        // 数字形式："key":100
        Match num = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*(-?\\d+)");
        return num.Success ? num.Groups[1].Value : null;
    }

    /// <summary>
    /// 还原 JSON 字符串字面量中的转义序列（\n \t \r \/ \" \\ \b \f 及 \uXXXX）。
    /// </summary>
    protected static string UnescapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;

        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c != '\\' || i + 1 >= s.Length)
            {
                sb.Append(c);
                continue;
            }

            char next = s[++i];
            switch (next)
            {
                case '"': sb.Append('"'); break;
                case '\\': sb.Append('\\'); break;
                case '/': sb.Append('/'); break;
                case 'n': sb.Append('\n'); break;
                case 't': sb.Append('\t'); break;
                case 'r': sb.Append('\r'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'u':
                    if (i + 4 < s.Length
                        && int.TryParse(s.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                    {
                        sb.Append((char)code);
                        i += 4;
                    }
                    else
                    {
                        sb.Append(next);
                    }

                    break;
                default: sb.Append(next); break;
            }
        }

        return sb.ToString();
    }
}
