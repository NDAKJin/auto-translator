using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace nomadicooer.AutoTranslator;

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
    // 平台标识键名（由调用方负责翻译为显示名，本类不引用 RimWorld 翻译机制）。
    // public 供 AutoTranslatorStartup 反射读取，按 cred.PlatformId 匹配实例化。
    public const string PlatformNameKey = "AutoTranslator.Aliyun";

    private const string Host = "mt.cn-hangzhou.aliyuncs.com";
    private const string Endpoint = "https://" + Host + "/";
    private const string Action = "Translate";
    private const string Version = "2018-10-12";
    private const string FormatType = "text";
    private const string Scene = "general";

    /// <summary>
    /// 构造一个阿里云翻译平台实例。
    /// </summary>
    /// <param name="accessKeyId">阿里云 AccessKeyId。</param>
    /// <param name="accessKeySecret">阿里云 AccessKeySecret。</param>
    public AliyunTranslate(string accessKeyId, string accessKeySecret)
        : base(accessKeyId ?? string.Empty, accessKeySecret ?? string.Empty)
    {
    }

    /// <summary>平台标识键名（由调用方负责翻译为显示名）。</summary>
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

        string simpleName = GetSimpleName(rimWorldLangName);
        RimWorldToAliyun.TryGetValue(simpleName, out string code);
        return code;
    }

    /// <summary>
    /// 具体翻译：from/to 已是阿里云平台语言码，text 为非空原文。
    /// </summary>
    protected override TranslationResult TranslateCore(string from, string to, string text)
    {
        try
        {
            string response = SendRequest(from, to, text);

            // 先检查业务错误码：非 200 说明翻译失败，记录可读描述后返回原文
            string platformCode = ExtractField(response, "Code");
            if (!string.IsNullOrEmpty(platformCode) && platformCode != "200")
            {
                TranslationErrorCode code = MapAliyunErrorCode(platformCode);
                string err = "阿里云翻译错误 " + platformCode + ": " + DescribeError(ErrorMessages, platformCode);
                AutoLog.Warn("[AutoTranslator] " + err
                                + "，原始信息: " + ExtractField(response, "Message"));
                return TranslationResult.Fail(text, code, err);
            }

            string translated = ExtractTranslation(response);
            if (string.IsNullOrEmpty(translated))
            {
                AutoLog.Warn("[AutoTranslator] 阿里云翻译响应中未找到译文");
                return TranslationResult.Fail(text, TranslationErrorCode.ServiceUnavailable, "阿里云翻译响应中未找到译文");
            }

            return TranslationResult.Ok(translated);
        }
        catch (Exception ex)
        {
            AutoLog.Warn("[AutoTranslator] 阿里云翻译请求失败: " + ex.Message);
            return TranslationResult.Fail(text, TranslationErrorCode.ServiceUnavailable, "阿里云翻译请求失败: " + ex.Message);
        }
    }

    /// <summary>
    /// 将阿里云平台业务错误码映射为统一错误码（<see cref="TranslationErrorCode"/>）。
    /// 限流/超时/系统繁忙等瞬时故障 → RateLimited；其余参数/语种/权限类 → InvalidRequest。
    /// </summary>
    private static TranslationErrorCode MapAliyunErrorCode(string code)
    {
        if (code == "Throttling" || code == "Throttling.User" || code == "Throttling.Api")
        {
            return TranslationErrorCode.RateLimited;
        }

        switch (code)
        {
            // 可重试的临时故障（原判定为限流的码）
            case "10001": // 请求超时
            case "10002": // 系统错误
            case "10007": // 翻译失败
            case "10012": // 翻译服务调用失败
                return TranslationErrorCode.RateLimited;
            default:
                return TranslationErrorCode.InvalidRequest;
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
            { "AccessKeyId", AccessKey },
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
        string signature = HmacSha1Base64(SecretKey + "&", stringToSign);

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
                AutoLog.Warn("[AutoTranslator] 阿里云错误响应(" + (int)errResp.StatusCode + "): " + errBody);
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
    /// 阿里云机器翻译错误码 -> 中文描述。
    /// 依据官方错误码表（HttpCode 101~113 / 199 对应错误码 10001~10013 / 19999）。
    /// </summary>
    private static readonly Dictionary<string, string> ErrorMessages = new Dictionary<string, string>
    {
        { "10001", "请求超时" },
        { "10002", "系统错误" },
        { "10003", "原文解码失败，请检查原文是否 UrlEncode" },
        { "10004", "参数缺失" },
        { "10005", "语项不支持" },
        { "10006", "语种识别失败" },
        { "10007", "翻译失败" },
        { "10008", "字符长度过长" },
        { "10009", "子账号没有权限" },
        { "10010", "账号没有开通服务" },
        { "10011", "子账号服务失败" },
        { "10012", "翻译服务调用失败" },
        { "10013", "账号服务没有开通或者欠费" },
        { "19999", "未知异常" },
    };

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
    /// RimWorld 语言文件夹的英文名 -> 阿里云翻译语言码。
    /// 依据阿里云官方语种列表核对，仅收录 RimWorld Core/Languages 下的官方语言，
    /// 阿里云支持 200+ 语种但 RimWorld 用不到的不在此列出（避免无效条目）。
    /// 繁体中文用 zh-tw（对应官方列表「中文繁体 Traditional Chinese」）。
    /// 注意：Ukrainian（乌克兰语）不在阿里云官方语种列表中，故不映射，返回 null 由上层跳过。
    /// 未列出的返回 null。
    /// </summary>
    private static readonly Dictionary<string, string> RimWorldToAliyun = new Dictionary<string, string>
    {
        { "English", "en" },                 // English
        { "ChineseSimplified", "zh" },       // ChineseSimplified (简体中文)
        { "ChineseTraditional", "zh-tw" },   // ChineseTraditional (繁體中文)
        { "Catalan", "ca" },                 // Catalan (Català)
        { "Czech", "cs" },                   // Czech (Čeština)
        { "Danish", "da" },                  // Danish (Dansk)
        { "Dutch", "nl" },                   // Dutch (Nederlands)
        { "Estonian", "et" },                // Estonian (Eesti)
        { "Finnish", "fi" },                 // Finnish (Suomi)
        { "French", "fr" },                  // French (Français)
        { "German", "de" },                  // German (Deutsch)
        { "Greek", "el" },                   // Greek (Ελληνικά)
        { "Hungarian", "hu" },               // Hungarian (Magyar)
        { "Italian", "it" },                 // Italian (Italiano)
        { "Japanese", "ja" },                // Japanese (日本語)
        { "Korean", "ko" },                  // Korean (한국어)
        { "Norwegian", "no" },               // Norwegian (Norsk Bokmål)
        { "Polish", "pl" },                  // Polish (Polski)
        { "Portuguese", "pt" },              // Portuguese (Português)
        { "PortugueseBrazilian", "pt" },     // PortugueseBrazilian，阿里云无巴葡独立码，回退 pt
        { "Romanian", "ro" },                // Romanian (Română)
        { "Russian", "ru" },                 // Russian (Русский)
        { "Slovak", "sk" },                  // Slovak (Slovenčina)
        { "Spanish", "es" },                 // Spanish (Español(Castellano))
        { "SpanishLatin", "es" },            // SpanishLatin，阿里云无拉美西语独立码，回退 es
        { "Swedish", "sv" },                 // Swedish (Svenska)
        { "Turkish", "tr" },                 // Turkish (Türkçe)
        { "Vietnamese", "vi" },              // Vietnamese (tiếng Việt)
        // Ukrainian (Українська)：阿里云官方语种列表中无乌克兰语，不支持，故不映射
    };
}
