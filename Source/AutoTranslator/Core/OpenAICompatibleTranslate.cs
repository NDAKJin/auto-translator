using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace nomadicooer.AutoTranslator;

/// <summary>通过 OpenAI 兼容的 Chat Completions 接口翻译单条游戏文本。</summary>
public class OpenAICompatibleTranslate : TranslatePlatform
{
    public const string PlatformNameKey = "AutoTranslator.OpenAICompatible";

    private static readonly HttpClient SharedClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    private readonly string baseUrl;
    private readonly string model;
    private readonly HttpClient client;

    public OpenAICompatibleTranslate(string apiKey, string baseUrl, string model)
        : this(apiKey, baseUrl, model, SharedClient)
    {
    }

    internal OpenAICompatibleTranslate(string apiKey, string baseUrl, string model, HttpClient client)
        : base(apiKey ?? string.Empty, string.Empty)
    {
        this.baseUrl = baseUrl;
        this.model = model;
        this.client = client;
    }

    protected override bool HasCredentials => !string.IsNullOrWhiteSpace(AccessKey);

    public override string PlatformName => PlatformNameKey;

    public override string GetLangCode(string rimWorldLangFolderName)
    {
        string name = GetSimpleName(rimWorldLangFolderName);
        switch (name)
        {
            case "ChineseSimplified": return "Simplified Chinese";
            case "ChineseTraditional": return "Traditional Chinese";
            case "PortugueseBrazilian": return "Brazilian Portuguese";
            case "SpanishLatin": return "Latin American Spanish";
            default: return string.IsNullOrWhiteSpace(name) ? null : name;
        }
    }

    public static bool TryCreateEndpoint(string apiBaseUrl, out Uri endpoint)
    {
        endpoint = null;
        if (!Uri.TryCreate(apiBaseUrl?.Trim(), UriKind.Absolute, out Uri uri)
            || (uri.Scheme != Uri.UriSchemeHttps && (uri.Scheme != Uri.UriSchemeHttp || !uri.IsLoopback))
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        string path = uri.AbsolutePath.TrimEnd('/');
        if (path == string.Empty) path = "/v1";
        if (!path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            path += "/chat/completions";
        }
        endpoint = new UriBuilder(uri) { Path = path }.Uri;
        return true;
    }

    protected override TranslationResult TranslateCore(string from, string to, string text)
    {
        if (string.IsNullOrWhiteSpace(model) || !TryCreateEndpoint(baseUrl, out Uri endpoint))
        {
            return TranslationResult.Fail(text, TranslationErrorCode.InvalidRequest, "AI 中转站模型名或接口地址无效");
        }

        var payload = new ChatRequest
        {
            Model = model.Trim(),
            Messages = new[]
            {
                new ChatMessage
                {
                    Role = "system",
                    Content = $"Translate RimWorld game UI text from {from} to {to}. "
                        + "Preserve placeholders such as {0} and {ref:X}, XML tags, and line breaks. "
                        + "Return only the translated text, without explanations or quotation marks."
                },
                new ChatMessage { Role = "user", Content = text }
            }
        };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessKey.Trim());
            request.Content = new StringContent(Serialize(payload), Encoding.UTF8, "application/json");
            using HttpResponseMessage response = client.SendAsync(request).GetAwaiter().GetResult();
            string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                string error = ReadError(body);
                string reason = "AI 中转站 HTTP " + (int)response.StatusCode;
                if (!string.IsNullOrEmpty(error)) reason += ": " + error;
                TranslationErrorCode code = (int)response.StatusCode == 429
                    || response.StatusCode == HttpStatusCode.RequestTimeout
                    || (int)response.StatusCode >= 500
                    ? TranslationErrorCode.RateLimited
                    : TranslationErrorCode.InvalidRequest;
                return TranslationResult.Fail(text, code, reason);
            }

            ChatResponse result = Deserialize<ChatResponse>(body);
            string translated = result?.Choices != null && result.Choices.Length > 0
                ? result.Choices[0]?.Message?.Content?.Trim()
                : null;
            if (string.IsNullOrWhiteSpace(translated))
            {
                return TranslationResult.Fail(text, TranslationErrorCode.ServiceUnavailable, "AI 中转站未返回译文");
            }
            if (!PreservesPlaceholders(text, translated))
            {
                return TranslationResult.Fail(text, TranslationErrorCode.ServiceUnavailable, "AI 译文改变了格式占位符");
            }
            return TranslationResult.Ok(translated);
        }
        catch (Exception ex)
        {
            AutoLog.Warn("AI 中转站请求失败", ex);
            return TranslationResult.Fail(text, TranslationErrorCode.ServiceUnavailable, "AI 中转站请求失败: " + ex.Message);
        }
    }

    private static bool PreservesPlaceholders(string source, string translated)
    {
        var counts = new Dictionary<string, int>();
        foreach (Match match in Regex.Matches(source, @"\{[^}]*\}"))
        {
            counts.TryGetValue(match.Value, out int count);
            counts[match.Value] = count + 1;
        }
        foreach (Match match in Regex.Matches(translated, @"\{[^}]*\}"))
        {
            if (!counts.TryGetValue(match.Value, out int count) || count == 0) return false;
            counts[match.Value] = count - 1;
        }
        foreach (int count in counts.Values)
        {
            if (count != 0) return false;
        }
        return true;
    }

    private static string ReadError(string body)
    {
        try
        {
            string message = Deserialize<ErrorResponse>(body)?.Error?.Message;
            return message?.Length > 240 ? message.Substring(0, 240) : message;
        }
        catch (SerializationException)
        {
            return null;
        }
    }

    private static string Serialize<T>(T value)
    {
        using var stream = new MemoryStream();
        new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static T Deserialize<T>(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
    }

    [DataContract]
    private sealed class ChatRequest
    {
        [DataMember(Name = "model")]
        public string Model;

        [DataMember(Name = "messages")]
        public ChatMessage[] Messages;
    }

    [DataContract]
    private sealed class ChatMessage
    {
        [DataMember(Name = "role")]
        public string Role;

        [DataMember(Name = "content")]
        public string Content;
    }

    [DataContract]
    private sealed class ChatResponse
    {
        [DataMember(Name = "choices")]
        public Choice[] Choices;
    }

    [DataContract]
    private sealed class Choice
    {
        [DataMember(Name = "message")]
        public ChatMessage Message;
    }

    [DataContract]
    private sealed class ErrorResponse
    {
        [DataMember(Name = "error")]
        public ApiError Error;
    }

    [DataContract]
    private sealed class ApiError
    {
        [DataMember(Name = "message")]
        public string Message;
    }
}
