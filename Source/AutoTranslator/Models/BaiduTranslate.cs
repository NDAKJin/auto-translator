using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace nomadicooer.AutoTranslator.Models;

/// <summary>
/// 百度翻译开放平台。
/// 签名算法：MD5(appId + q + salt + secretKey)，拼到 GET 查询参数。
/// 相比火山 V4 签名简单很多，无需 HMAC 链，也无需 X-Content-Sha256/Host 头。
/// 请求地址：http://api.fanyi.baidu.com/api/trans/vip/translate
/// 本类不依赖 RimWorld（无 Verse 引用），凭证通过构造函数注入，便于独立测试与复用。
/// </summary>
public class BaiduTranslate : TranslatePlatform
{
    // 平台标识键名（由调用方负责翻译为显示名，本类不引用 RimWorld 翻译机制）
    private const string PlatformNameKey = "AutoTranslator.Baidu";

    private const string Endpoint = "http://api.fanyi.baidu.com/api/trans/vip/translate";

    private readonly string _appId;
    private readonly string _secretKey;
    private readonly Random _random = new Random();

    /// <summary>
    /// 构造一个百度翻译平台实例。
    /// </summary>
    /// <param name="appId">百度翻译 APP ID。</param>
    /// <param name="secretKey">百度翻译密钥。</param>
    public BaiduTranslate(string appId, string secretKey)
    {
        _appId = appId ?? string.Empty;
        _secretKey = secretKey ?? string.Empty;
    }

    /// <summary>
    /// 平台标识键名（由调用方负责翻译为显示名）。
    /// </summary>
    public override string PlatformName => PlatformNameKey;

    /// <summary>
    /// 将 RimWorld 语言文件夹名映射到百度翻译语言码。
    /// 百度支持的语种有限，未知语言返回 null（由上层跳过）。
    /// </summary>
    public override string GetLangCode(string rimWorldLangName)
    {
        if (string.IsNullOrEmpty(rimWorldLangName))
        {
            return null;
        }

        string simpleName = TranslatePlatform.GetSimpleName(rimWorldLangName);
        RimWorldToBaidu.TryGetValue(simpleName, out string code);
        return code;
    }

    /// <summary>
    /// 调用百度翻译 API，将 text 从 source 翻译到 target。
    /// 凭证通过构造函数注入；未配置或请求失败时记录日志并返回原文。
    /// 注意：当前为同步实现（会阻塞调用线程）。
    /// </summary>
    public override string Translate(string source, string target, string text)
    {
        if (string.IsNullOrEmpty(_appId) || string.IsNullOrEmpty(_secretKey))
        {
            Debug.WriteLine("[AutoTranslator] 百度翻译凭证未配置，返回原文。");
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
            string salt = _random.Next(100000).ToString(CultureInfo.InvariantCulture);
            string sign = Md5Hex(_appId + text + salt + _secretKey);

            string url = Endpoint
                         + "?q=" + Uri.EscapeDataString(text)
                         + "&from=" + from
                         + "&to=" + to
                         + "&appid=" + Uri.EscapeDataString(_appId)
                         + "&salt=" + salt
                         + "&sign=" + sign;

            string response = HttpGet(url);
            string translated = ExtractTranslation(response);
            return string.IsNullOrEmpty(translated) ? text : translated;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("[AutoTranslator] 百度翻译请求失败: " + ex.Message);
            return text;
        }
    }

    /// <summary>
    /// 发送 GET 请求并返回响应文本。
    /// </summary>
    private static string HttpGet(string url)
    {
        var request = (HttpWebRequest)WebRequest.Create(url);
        request.Method = "GET";
        request.Timeout = 6000;
        using var response = (HttpWebResponse)request.GetResponse();
        using var reader = new System.IO.StreamReader(response.GetResponseStream(), Encoding.GetEncoding("utf-8"));
        return reader.ReadToEnd();
    }

    /// <summary>
    /// 从百度响应 JSON 中提取译文：trans_result[0].dst。
    /// 失败时返回 null。
    /// </summary>
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

    /// <summary>
    /// 计算 MD5 小写 32 位十六进制。
    /// </summary>
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

    /// <summary>
    /// 将 JSON 字符串字面量内容反转义（\n、\"、\\、\/、\t、\r、\b、\f、\uXXXX）。
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
    /// RimWorld 语言文件夹的英文名 -> 百度翻译语言码。
    /// 百度支持语种有限，未列出的返回 null。
    /// 代码依据百度官方语种列表（cht=繁体、sk=斯洛伐克、cat=加泰罗尼亚等）。
    /// </summary>
    private static readonly Dictionary<string, string> RimWorldToBaidu = new Dictionary<string, string>
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
}
