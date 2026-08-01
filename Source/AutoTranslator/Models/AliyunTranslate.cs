using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace nomadicooer.AutoTranslator.Models;

/// <summary>
/// 阿里云机器翻译（Alimt）开放平台。
/// 调用 Action=Translate 的专业文本翻译接口（RPC 风格，POST 表单），
/// 请求体字段：SourceText / TargetLanguage / SourceLanguage / FormatType=text / Scene=general，
/// 响应 JSON 形如 {"Data":{"Translated":"...","WordCount":N,"DetectedLanguage":"zh"},"RequestId":"..."}。
/// 鉴权为阿里云 RPC V1 签名（HMAC-SHA1 + percent-encoding + 排序），
/// 自行实现签名逻辑，不引用阿里云官方 SDK（Tea / OpenApiClient），
/// 以避免在 RimWorld 的 Mono 运行时下加载整套 NuGet 依赖。
/// 本类不依赖 RimWorld（无 Verse 引用），凭证通过构造函数注入，便于独立测试与复用。
/// </summary>
public class AliyunTranslate : TranslatePlatform
{
    // 平台标识键名（由调用方负责翻译为显示名，本类不引用 RimWorld 翻译机制）
    private const string PlatformNameKey = "AutoTranslator.Aliyun";

    private const string Host = "mt.cn-hangzhou.aliyuncs.com";
    private const string Endpoint = "https://" + Host + "/";
    private const string Action = "Translate";
    private const string Version = "2018-10-12";
    private const string FormatType = "text";
    private const string Scene = "general";

    private readonly string _accessKeyId;
    private readonly string _accessKeySecret;

    /// <summary>
    /// 构造一个阿里云翻译平台实例。
    /// </summary>
    /// <param name="accessKeyId">阿里云 AccessKeyId。</param>
    /// <param name="accessKeySecret">阿里云 AccessKeySecret。</param>
    public AliyunTranslate(string accessKeyId, string accessKeySecret)
    {
        _accessKeyId = accessKeyId ?? string.Empty;
        _accessKeySecret = accessKeySecret ?? string.Empty;
    }

    /// <summary>
    /// 平台标识键名（由调用方负责翻译为显示名）。
    /// </summary>
    public override string PlatformName => PlatformNameKey;

    /// <summary>
    /// RimWorld 语言简单名 → 阿里云翻译语言码。
    /// 阿里云通用版支持语种以标准码为主，繁中用 zh-tw。
    /// 未知语言返回 null（由上层跳过）。
    /// </summary>
    public override string GetLangCode(string rimWorldLangName)
    {
        if (string.IsNullOrEmpty(rimWorldLangName))
        {
            return null;
        }

        string simpleName = TranslatePlatform.GetSimpleName(rimWorldLangName);
        RimWorldToAliyun.TryGetValue(simpleName, out string code);
        return code;
    }

    /// <summary>
    /// 调用阿里云翻译 API，将 text 从 source 翻译到 target。
    /// 凭证通过构造函数注入；未配置或请求失败时记录日志并返回原文。
    /// 注意：当前为同步实现（会阻塞调用线程）。
    /// </summary>
    public override string Translate(string source, string target, string text)
    {
        if (string.IsNullOrEmpty(_accessKeyId) || string.IsNullOrEmpty(_accessKeySecret))
        {
            Debug.WriteLine("[AutoTranslator] 阿里云翻译凭证未配置，返回原文。");
            return text;
        }

        string from = GetLangCode(source);
        string to = GetLangCode(target);
        if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to) || string.IsNullOrEmpty(text))
        {
            return text;
        }

        try
        {
            string response = SendRequest(from, to, text);
            string translated = ExtractTranslation(response);
            return string.IsNullOrEmpty(translated) ? text : translated;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("[AutoTranslator] 阿里云翻译请求失败: " + ex.Message);
            return text;
        }
    }

    /// <summary>
    /// 构造签名并发送 RPC 请求，返回响应体文本。
    /// </summary>
    private string SendRequest(string sourceLang, string targetLang, string text)
    {
        string timestamp = NowIso8601();

        // RPC 公共参数 + 业务参数，全部参与签名
        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            { "Format", "JSON" },
            { "Version", Version },
            { "AccessKeyId", _accessKeyId },
            { "SignatureMethod", "HMAC-SHA1" },
            { "SignatureVersion", "1.0" },
            { "SignatureNonce", Guid.NewGuid().ToString("N") },
            { "Timestamp", timestamp },
            { "Action", Action },
            { "RegionId", "cn-hangzhou" },
            { "SourceText", text },
            { "SourceLanguage", sourceLang },
            { "TargetLanguage", targetLang },
            { "FormatType", FormatType },
            { "Scene", Scene },
        };

        // 第一步：对每个参数做 RFC3986 编码并拼接为规范化查询串
        var canonicalParts = new List<string>(parameters.Count);
        foreach (var kv in parameters)
        {
            canonicalParts.Add(PercentEncode(kv.Key) + "=" + PercentEncode(kv.Value));
        }
        string canonicalQuery = string.Join("&", canonicalParts);

        // 第二步：构造待签字符串 StringToSign
        string stringToSign = "POST&" + PercentEncode("/") + "&" + PercentEncode(canonicalQuery);

        // 第三步：HMAC-SHA1，密钥为 AccessKeySecret + "&"，结果做 Base64（阿里云要求，非 Hex）
        string signature = HmacSha1Base64(_accessKeySecret + "&", stringToSign);

        // 第四步：把签名追加到查询参数
        string finalQuery = canonicalQuery + "&" + PercentEncode("Signature") + "=" + PercentEncode(signature);

        return HttpPost(finalQuery);
    }

    /// <summary>
    /// 发送 POST 表单请求到阿里云 Endpoint。
    /// </summary>
    private static string HttpPost(string formBody)
    {
        var request = (HttpWebRequest)WebRequest.Create(Endpoint);
        request.Method = "POST";
        request.ContentType = "application/x-www-form-urlencoded; charset=utf-8";
        request.Timeout = 6000;

        byte[] data = Encoding.UTF8.GetBytes(formBody);
        using (var stream = request.GetRequestStream())
        {
            stream.Write(data, 0, data.Length);
        }

        try
        {
            using var response = (HttpWebResponse)request.GetResponse();
            using var reader = new StreamReader(response.GetResponseStream(), Encoding.GetEncoding("utf-8"));
            return reader.ReadToEnd();
        }
        catch (WebException wex)
        {
            if (wex.Response is HttpWebResponse errResp)
            {
                using var errReader = new StreamReader(errResp.GetResponseStream(), Encoding.GetEncoding("utf-8"));
                string errBody = errReader.ReadToEnd();
                Debug.WriteLine("[AutoTranslator] 阿里云错误响应(" + (int)errResp.StatusCode + "): " + errBody);
                return errBody;
            }

            throw;
        }
    }

    /// <summary>
    /// 从响应 JSON 中提取译文：Data.Translated。失败时返回 null。
    /// </summary>
    private static string ExtractTranslation(string response)
    {
        if (string.IsNullOrEmpty(response))
        {
            return null;
        }

        Match m = Regex.Match(response, "\"Translated\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
        return m.Success ? UnescapeJson(m.Groups[1].Value) : null;
    }

    /// <summary>
    /// 阿里云要求的 RFC3986 percent-encoding：
    /// 除 A-Za-z0-9 与 -_.~ 外均编码为 %XX（大写），空格编为 %20（非 +）。
    /// </summary>
    private static string PercentEncode(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length * 2);
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        foreach (byte b in bytes)
        {
            char c = (char)b;
            if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')
                || c == '-' || c == '_' || c == '.' || c == '~')
            {
                sb.Append(c);
            }
            else
            {
                sb.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// 计算 HMAC-SHA1 并做 Base64 编码（阿里云 V1 签名要求）。
    /// </summary>
    private static string HmacSha1Base64(string key, string data)
    {
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(key));
        byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// 生成阿里云要求的 ISO8601 UTC 时间戳，形如 2026-08-01T12:00:00Z。
    /// </summary>
    private static string NowIso8601()
    {
        return DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 将 JSON 字符串字面量内容反转义。
    /// </summary>
    private static string UnescapeJson(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return s;
        }

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
                    if (i + 4 < s.Length)
                    {
                        string hex = s.Substring(i + 1, 4);
                        if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                        {
                            sb.Append((char)code);
                            i += 4;
                        }
                        else
                        {
                            sb.Append(next);
                        }
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

    /// <summary>
    /// RimWorld 语言文件夹的英文名 -> 阿里云翻译语言码。
    /// 覆盖 Core/Languages 目录下主流官方语言；繁中阿里云用 zh-tw。
    /// 未列出的返回 null。
    /// </summary>
    private static readonly Dictionary<string, string> RimWorldToAliyun = new Dictionary<string, string>
    {
        { "English", "en" },
        { "ChineseSimplified", "zh" },
        { "ChineseTraditional", "zh-tw" },
        { "Catalan", "ca" },
        { "Czech", "cs" },
        { "Danish", "da" },
        { "Dutch", "nl" },
        { "Estonian", "et" },
        { "Finnish", "fi" },
        { "French", "fr" },
        { "German", "de" },
        { "Greek", "el" },
        { "Hungarian", "hu" },
        { "Italian", "it" },
        { "Japanese", "ja" },
        { "Korean", "ko" },
        { "Norwegian", "no" },
        { "Polish", "pl" },
        { "Portuguese", "pt" },
        { "PortugueseBrazilian", "pt" },
        { "Romanian", "ro" },
        { "Russian", "ru" },
        { "Slovak", "sk" },
        { "Spanish", "es" },
        { "SpanishLatin", "es" },
        { "Swedish", "sv" },
        { "Turkish", "tr" },
        { "Ukrainian", "uk" },
        { "Vietnamese", "vi" },
        { "Thai", "th" },
        { "Arabic", "ar" },
        { "Indonesian", "id" },
        { "Malay", "ms" },
        { "Hindi", "hi" },
    };
}
