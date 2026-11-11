using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace nomadicooer.AutoTranslator;

/// <summary>
/// 翻译平台抽象基类，实现 <see cref="ITranslationPlatform"/> 并承载通用前置校验（模板方法）。
/// <list type="bullet">
///   <item><description>sealed <see cref="Translate"/> 统一做凭证/空文本/语言码校验，解析出平台语言码后调用抽象 <see cref="TranslateCore"/>。</description></item>
///   <item><description>派生类只需实现 <see cref="TranslateCore"/>、<see cref="PlatformName"/>、<see cref="GetLangCode"/>，不再重复校验逻辑。</description></item>
///   <item><description>提供 <see cref="FailLog"/> / <see cref="DescribeError"/> 等共用辅助，消除各引擎重复代码。</description></item>
/// </list>
/// 节流与限流重试统一由 TranslationDispatcher 负责，本基类不处理。
/// </summary>
public abstract class TranslatePlatform : ITranslationPlatform
{
    protected TranslatePlatform(string accessKey)
    {
        AccessKey = accessKey;
    }

    /// <summary>Access Key / APP ID</summary>
    protected string AccessKey { get; }

    protected virtual bool HasCredentials => !string.IsNullOrWhiteSpace(AccessKey);

    /// <summary>平台标识（带前缀的完整标志）。</summary>
    public abstract string PlatformName { get; }

    /// <summary>将 RimWorld 语言名转为平台语言码；不支持时返回 null。</summary>
    public abstract string GetLangCode(string rimWorldLangFolderName);

    /// <summary>
    /// 模板方法：先统一校验，再解析语言码，最后委派给 <see cref="TranslateCore"/>。
    /// 派生类不应重写。
    /// </summary>
    public TranslationResult Translate(string source, string target, string text)
    {
        if (!HasCredentials)
        {
            return FailLog("凭证未配置", TranslationErrorCode.InvalidRequest, null);
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return FailLog("待翻译文本为空", TranslationErrorCode.InvalidRequest, null);
        }

        string from = GetLangCode(source);
        if (string.IsNullOrEmpty(from))
        {
            return FailLog($"不支持的源语言: {source}", TranslationErrorCode.InvalidRequest, null);
        }

        string to = GetLangCode(target);
        if (string.IsNullOrEmpty(to))
        {
            return FailLog($"不支持的目标语言: {target}", TranslationErrorCode.InvalidRequest, null);
        }

        return TranslateCore(from, to, text);
    }

    /// <summary>具体翻译逻辑：from/to 已是平台语言码，text 为非空待译原文。</summary>
    protected abstract TranslationResult TranslateCore(string from, string to, string text);

    /// <summary>记录失败日志并返回失败结果（译文回退为原文 text）。</summary>
    protected static TranslationResult FailLog(string message, TranslationErrorCode code, string rawError)
    {
        AutoLog.Warn(message);
        return TranslationResult.Fail(string.Empty, code, rawError ?? message);
    }

    /// <summary>
    /// 将平台错误码翻译为可读中文描述（含解决方案提示）。
    /// 未收录的错误码原样返回，便于排查。
    /// </summary>
    protected static string DescribeError(IReadOnlyDictionary<string, string> errorMessages, string code)
    {
        return errorMessages.TryGetValue(code, out string desc) ? desc : code;
    }

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

    // 静态编译的 Regex 实例，避免每次请求 new Regex 的开销
    private static readonly Regex StringFieldRegex =
        new("\"(?<key>[^\"]+)\"\\s*:\\s*\"(?<val>(?:\\\\.|[^\"\\\\])*)\"", RegexOptions.Compiled);

    private static readonly Regex NumberFieldRegex =
        new("\"(?<key>[^\"]+)\"\\s*:\\s*(?<val>-?\\d+)", RegexOptions.Compiled);

    /// <summary>
    /// 从扁平 JSON 文本中提取某字段的值（不依赖外部 JSON 库）。
    /// 同时支持字符串字面量（"key":"value"，处理转义）与数字字面量（"key":100）。
    /// 找不到时返回 null。
    /// </summary>
    protected static string ExtractField(string json, string key)
    {
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return null;

        Match str = StringFieldRegex.Match(json);
        while (str.Success)
        {
            if (str.Groups["key"].Value == key)
            {
                return UnescapeJson(str.Groups["val"].Value);
            }

            str = str.NextMatch();
        }

        Match num = NumberFieldRegex.Match(json);
        while (num.Success)
        {
            if (num.Groups["key"].Value == key)
            {
                return num.Groups["val"].Value;
            }

            num = num.NextMatch();
        }

        return null;
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
