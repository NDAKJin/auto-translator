using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace nomadicooer.AutoTranslator.Models;

/// <summary>
/// 火山引擎（Volcengine）机器翻译平台。
/// 将 RimWorld 语言文件夹名映射到火山翻译的语言码，并调用火山文本翻译 API。
/// 协议依据火山 translate 2025-03-01 SDK：POST /TranslateText/2025-03-01/translate/post/application_json/，
/// 请求体 {SourceLanguage, TargetLanguage, TextList}，响应 TranslationList[0].Translation。
/// 鉴权为火山 V4 签名（volcengineSign）。
/// 本类不依赖 RimWorld（无 Verse 引用），凭证通过构造函数注入，便于独立测试与复用。
/// </summary>
public class VolcengineTranslate : TranslatePlatform
{
    // 平台标识键名（由调用方负责翻译为显示名，本类不引用 RimWorld 翻译机制）
    private const string PlatformNameKey = "AutoTranslator.Volcengine";

    // 火山翻译 API 固定参数（cn-north-1 为机器翻译服务区）
    private const string Schema = "https";
    private const string Host = "translate.volcengineapi.com";
    private const string ApiPath = "/TranslateText/2025-03-01/translate/post/application_json/";
    private const string Service = "translate";
    private const string Region = "cn-north-1";
    private const string Action = "TranslateText";
    private const string Version = "2025-03-01";
    private const string ContentType = "application/json";
    private const string Algorithm = "HMAC-SHA256";
    private const string SignedHeaders = "content-type;host;x-content-sha256;x-date";

    private readonly string _accessKey;
    private readonly string _secretKey;

    /// <summary>
    /// 构造一个火山翻译平台实例。
    /// </summary>
    /// <param name="accessKey">火山引擎 AccessKey。</param>
    /// <param name="secretKey">火山引擎 SecretKey。</param>
    public VolcengineTranslate(string accessKey, string secretKey)
    {
        _accessKey = accessKey ?? string.Empty;
        _secretKey = secretKey ?? string.Empty;
    }

    /// <summary>
    /// 平台标识键名（由调用方负责翻译为显示名）。
    /// </summary>
    public override string PlatformName => PlatformNameKey;

    /// <summary>
    /// RimWorld 语言简单名 → 火山翻译语言码。
    /// 键为 <see cref="TranslatePlatform.GetSimpleName"/> 解析出的简单名（如 "ChineseSimplified"）。
    /// 覆盖 Core/Languages 目录下所有 RimWorld 官方语言；其中 PortugueseBrazilian、SpanishLatin
    /// 火山无专门码，回退到 pt / es。
    /// </summary>
    private static readonly Dictionary<string, string> RimWorldToVolcengine = new Dictionary<string, string>
    {
        { "English", "en" },
        { "ChineseSimplified", "zh" },
        { "ChineseTraditional", "zh-Hant" },
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
    };

    /// <summary>
    /// 将 RimWorld 语言文件夹名（如 "ChineseSimplified (简体中文)"）转换为火山翻译语言码。
    /// 未知语言返回 null。
    /// </summary>
    public override string GetLangCode(string name)
    {
        string simple = TranslatePlatform.GetSimpleName(name);
        return RimWorldToVolcengine.TryGetValue(simple, out string code) ? code : null;
    }

    /// <summary>
    /// 调用火山文本翻译 API，将 text 从 source 翻译到 target。
    /// 凭证通过构造函数注入；未配置或请求失败时记录日志并返回原文。
    /// 注意：当前为同步实现（会阻塞调用线程），联调/正式版应改为异步。
    /// </summary>
    public override string Translate(string source, string target, string text)
    {
        if (string.IsNullOrEmpty(_accessKey) || string.IsNullOrEmpty(_secretKey))
        {
            Debug.WriteLine("[AutoTranslator] 火山翻译凭证未配置，返回原文。");
            return text;
        }

        // 火山翻译不支持 auto 自动检测，source 必须为目标语言之外的具体语言码
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target) || string.IsNullOrEmpty(text))
        {
            return text;
        }

        string payload = "{\"SourceLanguage\":\"" + JsonEscape(source)
                         + "\",\"TargetLanguage\":\"" + JsonEscape(target)
                         + "\",\"TextList\":[\"" + JsonEscape(text) + "\"]}";

        try
        {
            string response = SendRequest(payload, _accessKey, _secretKey);
            string translated = ExtractTranslation(response);
            return string.IsNullOrEmpty(translated) ? text : translated;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("[AutoTranslator] 火山翻译请求失败: " + ex.Message);
            return text;
        }
    }

    private static string SendRequest(string payload, string accessKey, string secretKey)
    {
        byte[] body = Encoding.UTF8.GetBytes(payload);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string xDate = now.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        string shortDate = xDate.Substring(0, 8);
        string payloadHash = Sha256Hex(body);

        // Action/Version 作为 query 参数参与签名（火山 V4 签名要求）
        var queryParams = new SortedDictionary<string, string>
        {
            { "Action", Action },
            { "Version", Version },
        };
        string query = string.Join("&", queryParams.Select(kv =>
            Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value)));

        string canonicalHeaders =
            "content-type:" + ContentType + "\n" +
            "host:" + Host + "\n" +
            "x-content-sha256:" + payloadHash + "\n" +
            "x-date:" + xDate + "\n";

        string canonicalRequest = "POST\n" + ApiPath + "\n" + query + "\n"
                                   + canonicalHeaders + "\n"
                                   + SignedHeaders + "\n" + payloadHash;

        string scope = shortDate + "/" + Region + "/" + Service + "/request";
        string stringToSign = Algorithm + "\n" + xDate + "\n" + scope + "\n"
                              + Sha256Hex(Encoding.UTF8.GetBytes(canonicalRequest));

        byte[] signingKey = GetSignatureKey(secretKey, shortDate, Region, Service);
        string signature = HmacHex(signingKey, stringToSign);

        string authorization = Algorithm
                               + " Credential=" + accessKey + "/" + scope
                               + ", SignedHeaders=" + SignedHeaders
                               + ", Signature=" + signature;

        var url = new Uri(Schema + "://" + Host + ApiPath + "?" + query);
        using (var client = new HttpClient())
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new ByteArrayContent(body),
            };
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(ContentType);
            request.Headers.TryAddWithoutValidation("Host", Host);
            request.Headers.Add("X-Date", xDate);
            request.Headers.Add("X-Content-Sha256", payloadHash);
            request.Headers.TryAddWithoutValidation("Authorization", authorization);

            HttpResponseMessage resp = client.SendAsync(request).GetAwaiter().GetResult();
            return resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }
    }

    private static string ExtractTranslation(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        // 取 TranslationList 中第一个 Translation 字段
        Match match = Regex.Match(
            json,
            "\"Translation\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
        return match.Success ? UnescapeJson(match.Groups[1].Value) : null;
    }

    private static byte[] GetSignatureKey(string key, string dateStamp, string region, string service)
    {
        byte[] kDate = Hmac(Encoding.UTF8.GetBytes(key), dateStamp);
        byte[] kRegion = Hmac(kDate, region);
        byte[] kService = Hmac(kRegion, service);
        return Hmac(kService, "request");
    }

    private static byte[] Hmac(byte[] key, string data)
    {
        using (var hmac = new HMACSHA256(key))
        {
            return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        }
    }

    private static byte[] Hmac(byte[] key, byte[] data)
    {
        using (var hmac = new HMACSHA256(key))
        {
            return hmac.ComputeHash(data);
        }
    }

    private static string HmacHex(byte[] key, string data)
    {
        byte[] hash = Hmac(key, data);
        return ToHex(hash);
    }

    private static string Sha256Hex(byte[] data)
    {
        using (var sha = SHA256.Create())
        {
            return ToHex(sha.ComputeHash(data));
        }
    }

    private static string ToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (byte b in bytes)
        {
            sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    private static string JsonEscape(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        return sb.ToString();
    }

    private static string UnescapeJson(string s)
    {
        if (!s.Contains("\\"))
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
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'u':
                    if (i + 4 < s.Length)
                    {
                        string hex = s.Substring(i + 1, 4);
                        sb.Append((char)int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                    }
                    break;
                default: sb.Append(next); break;
            }
        }
        return sb.ToString();
    }
}
