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
/// 百度翻译平台。签名：MD5(appId + q + salt + secretKey) 拼到 GET 参数；不依赖 RimWorld，凭证由构造函数注入。
/// </summary>
/// <param name="appId">百度翻译 APP ID。</param>
/// <param name="secretKey">百度翻译密钥。</param>
public class BaiduTranslate(string appId, string secretKey) 
: TranslatePlatform(appId ?? string.Empty, secretKey ?? string.Empty)
{
    /// <summary>平台标识键名，供 AutoTranslatorStartup 反射读取匹配实例化。</summary>
    public const string PlatformNameKey = "AutoTranslator.Baidu";

    private const string Endpoint = "http://api.fanyi.baidu.com/api/trans/vip/translate";

    private readonly Random _random = new();

    /// <summary>可重试的临时故障码集合，命中映射为 RateLimited。</summary>
    private static readonly HashSet<string> RetryableErrorCodes =
    [
        "54003", // 访问频率受限
        "59004", // QPS 超限
        "54005", // 长 query 请求频繁
        "52001", // 请求超时
        "52002", // 系统错误
        "52003", // 未授权（服务端临时）→ 归为临时故障，重试可能恢复
    ];

    /// <summary>百度错误码 -> 中文描述（52000 为成功，不在失败分支使用）。</summary>
    private static readonly Dictionary<string, string> ErrorMessages = new()
    {
        { "52000", "成功" },
        { "52001", "请求超时，请检查原文及语种是否受支持" },
        { "52002", "系统错误，请重试" },
        { "52003", "未授权用户，请检查 appid 是否正确或服务是否已开通" },
        { "54000", "必填参数为空，请检查是否漏传或误传参数" },
        { "54001", "签名错误或 token 错误，请检查签名生成方法" },
        { "54003", "访问频率受限，请降低调用频率或升级为高级版/尊享版" },
        { "54004", "账户余额不足，请前往管理控制台充值" },
        { "54005", "长 query 请求频繁，请降低超 1 万字节请求的频率，3 秒后再试" },
        { "58000", "客户端 IP 非法，请检查开发者信息页填写的服务器 IP" },
        { "58001", "译文语言方向不支持，标准版/高级版仅支持 28 个常见语种" },
        { "58002", "服务当前已关闭，请前往管理控制台开启服务" },
        { "58003", "此 IP 已被封禁（同一 IP 当日使用多个 APPID），次日解封" },
        { "58004", "模型参数错误，model_type 须为 llm 或 nmt" },
        { "59002", "翻译指令过长，reference 参数超过 500 字符上限" },
        { "59003", "请求文本过长，q 参数超过 6000 字符上限" },
        { "59004", "QPS 超限，当前接口 QPS 已触及上限" },
        { "59005", "tag_handling 参数非法，须为 0 或 1" },
        { "59006", "标签解析失败，标签未闭合或为空" },
        { "59007", "ignore_tags 长度超限，上限为 20" },
        { "90107", "认证未通过或未生效，请前往「我的认证」查看进度" },
        { "20003", "请求内容存在安全风险，请检查文本是否涉及违规内容" },
    };

    /// <summary>RimWorld 语言名 -> 百度语言码，未列出返回 null。</summary>
    private static readonly Dictionary<string, string> RimWorldToBaidu = new()
    {
        { "English", "en" },
        { "ChineseSimplified", "zh" },
        { "ChineseTraditional", "cht" },
        { "Catalan", "cat" },
        { "Arabic", "ara" },
        { "Bulgarian", "bul" },
        { "Czech", "cs" },
        { "Danish", "dan" },
        { "Dutch", "nl" },
        { "Estonian", "est" },
        { "Finnish", "fin" },
        { "French", "fra" },
        { "German", "de" },
        { "Greek", "el" },
        { "Hungarian", "hu" },
        { "Italian", "it" },
        { "Japanese", "jp" },
        { "Korean", "kor" },
        { "Norwegian", "nor" },
        { "Polish", "pl" },
        { "Portuguese", "pt" },
        { "PortugueseBrazilian", "pt" },
        { "Romanian", "rom" },
        { "Russian", "ru" },
        { "Slovak", "sk" },
        { "Spanish", "spa" },
        { "SpanishLatin", "spa" },
        { "Swedish", "swe" },
        { "Thai", "th" },
        { "Turkish", "tr" },
        { "Ukrainian", "ukr" },
        { "Vietnamese", "vie" },
    };

    /// <summary>平台标识键名。</summary>
    public override string PlatformName => PlatformNameKey;

    /// <summary>RimWorld 语言名 -> 百度语言码，未知返回 null。</summary>
    public override string GetLangCode(string rimWorldLangName)
    {
        if (string.IsNullOrEmpty(rimWorldLangName))
        {
            return null;
        }

        string simpleName = GetSimpleName(rimWorldLangName);
        RimWorldToBaidu.TryGetValue(simpleName, out string code);
        return code;
    }

    /// <summary>核心翻译：from/to 为百度语言码，text 为非空原文。</summary>
    protected override TranslationResult TranslateCore(string from, string to, string text)
    {
        try
        {
            string salt = _random.Next(100000).ToString(CultureInfo.InvariantCulture);
            string sign = Md5Hex(AccessKey + text + salt + SecretKey);

            string url = Endpoint
                         + "?q=" + Uri.EscapeDataString(text)
                         + "&from=" + from
                         + "&to=" + to
                         + "&appid=" + Uri.EscapeDataString(AccessKey)
                         + "&salt=" + salt
                         + "&sign=" + sign;

            string response = HttpGet(url);

            // 先检查业务错误码：成功时无 error_code 字段或为 52000，其余均为失败
            string platformCode = ExtractField(response, "error_code");
            if (!string.IsNullOrEmpty(platformCode) && platformCode != "52000")
            {
                TranslationErrorCode code = MapBaiduErrorCode(platformCode);
                string err = "百度翻译错误 " + platformCode + ": " + DescribeError(ErrorMessages, platformCode);
                AutoLog.Warn("[AutoTranslator] " + err
                                + "，原始信息: " + ExtractField(response, "error_msg"));
                return TranslationResult.Fail(text, code, err);
            }

            string translated = ExtractTranslation(response);
            if (string.IsNullOrEmpty(translated))
            {
                AutoLog.Warn("[AutoTranslator] 百度翻译响应中未找到译文");
                return TranslationResult.Fail(text, TranslationErrorCode.ServiceUnavailable, "百度翻译响应中未找到译文");
            }

            return TranslationResult.Ok(translated);
        }
        catch (Exception ex)
        {
            AutoLog.Warn("[AutoTranslator] 百度翻译请求失败: " + ex.Message);
            return TranslationResult.Fail(text, TranslationErrorCode.ServiceUnavailable, "百度翻译请求失败: " + ex.Message);
        }
    }

    /// <summary>百度业务错误码 -> 统一错误码（瞬时故障 RateLimited，其余 InvalidRequest）。</summary>
    private static TranslationErrorCode MapBaiduErrorCode(string code)
    {
        return RetryableErrorCodes.Contains(code)
            ? TranslationErrorCode.RateLimited
            : TranslationErrorCode.InvalidRequest;
    }

    /// <summary>发送 GET 请求并返回响应文本。</summary>
    private static string HttpGet(string url)
    {
        var request = (HttpWebRequest)WebRequest.Create(url);
        request.Method = "GET";
        request.Timeout = 6000;
        using var response = (HttpWebResponse)request.GetResponse();
        using var reader = new StreamReader(response.GetResponseStream(), Encoding.GetEncoding("utf-8"));
        return reader.ReadToEnd();
    }

    /// <summary>从响应 JSON 提取译文（trans_result[0].dst），失败返回 null。</summary>
    private static string ExtractTranslation(string response)
    {
        if (string.IsNullOrEmpty(response))
        {
            return null;
        }

        Match m = Regex.Match(response, "\"dst\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
        if (!m.Success)
        {
            return null;
        }

        return UnescapeJson(m.Groups[1].Value);
    }

    /// <summary>计算 MD5 小写 32 位十六进制。</summary>
    private static string Md5Hex(string input)
    {
        using MD5 md5 = MD5.Create();
        byte[] bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        var sb = new StringBuilder();
        foreach (byte b in bytes)
        {
            sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

}
