using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using nomadicooer.AutoTranslator;
using Xunit;

namespace AutoTranslator.Tests;

public class OpenAICompatibleTranslateTests
{
    [Fact]
    public void TimeoutIsReportedForConnectionAndTranslation()
    {
        using var client = new HttpClient(new ThrowingHandler(new TaskCanceledException()));
        var platform = new OpenAICompatibleTranslate("key", "https://relay.example/v1", "model", client);
        Assert.Contains("API 调用超时", platform.TestConnection().RawError);
        Assert.Contains("API 调用超时", platform.Translate("English", "ChineseSimplified", "Hello").RawError);
    }
    [Fact]
    public void EncodingErrorRetriesUsingDirectClient()
    {
        using var primary = new HttpClient(new ThrowingHandler(new ArgumentException("Illegal byte sequence encounted in the input.")));
        var fallback = new StubHandler(HttpStatusCode.OK, "{\"choices\":[{\"message\":{\"content\":\"Hello\"}}]}");
        using var direct = new HttpClient(fallback);
        var platform = new OpenAICompatibleTranslate("key", "https://relay.example/v1", "model", primary, direct);
        Assert.True(platform.TestConnection().Success);
        Assert.NotNull(fallback.Url);
    }

    [Fact]
    public void OtherErrorsDoNotRetryDirectly()
    {
        using var primary = new HttpClient(new ThrowingHandler(new HttpRequestException("offline")));
        var fallback = new StubHandler(HttpStatusCode.OK, "{}");
        using var direct = new HttpClient(fallback);
        var platform = new OpenAICompatibleTranslate("key", "https://relay.example/v1", "model", primary, direct);
        Assert.False(platform.TestConnection().Success);
        Assert.Null(fallback.Url);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly Exception exception;
        public ThrowingHandler(Exception exception) => this.exception = exception;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromException<HttpResponseMessage>(exception);
    }

    [Theory]
    [InlineData("Hello!")]
    [InlineData("请提供需要翻译的文本")]
    public void ConnectionTestAcceptsAnyNonemptyModelContent(string content)
    {
        var response = JsonSerializer.Serialize(new { choices = new[] { new { message = new { content } } } });
        var handler = new StubHandler(HttpStatusCode.OK, response);
        using var client = new HttpClient(handler);
        var platform = new OpenAICompatibleTranslate("test-key", "https://relay.example/v1", "test-model", client);
        Assert.True(platform.TestConnection().Success);
        using JsonDocument request = JsonDocument.Parse(handler.Body);
        Assert.False(request.RootElement.TryGetProperty("response_format", out _));
        Assert.Equal("Hello", request.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
    }

    [Fact]
    public void ConnectionTestRejectsEmptyResponseAndHttpFailure()
    {
        using var emptyClient = new HttpClient(new StubHandler(HttpStatusCode.OK, "{\"choices\":[{\"message\":{\"content\":\"\"}}]}"));
        using var errorClient = new HttpClient(new StubHandler(HttpStatusCode.Unauthorized, "{}"));
        Assert.False(new OpenAICompatibleTranslate("key", "https://relay.example/v1", "model", emptyClient).TestConnection().Success);
        Assert.False(new OpenAICompatibleTranslate("key", "https://relay.example/v1", "model", errorClient).TestConnection().Success);
    }
    [Theory]
    [InlineData("https://relay.example/v1", "https://relay.example/v1/chat/completions")]
    [InlineData("https://relay.example/v1/chat/completions", "https://relay.example/v1/chat/completions")]
    [InlineData("http://localhost:8080", "http://localhost:8080/v1/chat/completions")]
    public void EndpointAcceptsCompatibleBaseUrls(string input, string expected)
    {
        Assert.True(OpenAICompatibleTranslate.TryCreateEndpoint(input, out Uri endpoint));
        Assert.Equal(expected, endpoint.AbsoluteUri);
    }

    [Theory]
    [InlineData("http://relay.example/v1")]
    [InlineData("https://relay.example/v1?api_key=secret")]
    [InlineData("https://user:password@relay.example/v1")]
    [InlineData("not a URL")]
    public void EndpointRejectsUnsafeOrInvalidUrls(string input)
    {
        Assert.False(OpenAICompatibleTranslate.TryCreateEndpoint(input, out _));
    }

    [Fact]
    public void SendsChatCompletionAndReadsTranslation()
    {
        var handler = new StubHandler(HttpStatusCode.OK,
            "{\"choices\":[{\"message\":{\"content\":\"{\\\"decision\\\":\\\"成功\\\",\\\"translatedText\\\":\\\"你好 {0}\\\\n世界\\\",\\\"reason\\\":\\\"\\\"}\"}}]}");
        using var client = new HttpClient(handler);
        var platform = new OpenAICompatibleTranslate("test-key", "https://relay.example/v1", "test-model", client);

        TranslationResult result = platform.Translate("English", "ChineseSimplified (简体中文)", "Hello {0}\nworld");

        Assert.True(result.Success);
        Assert.Equal("你好 {0}\n世界", result.TranslatedText);
        Assert.Equal("https://relay.example/v1/chat/completions", handler.Url);
        Assert.Equal("Bearer test-key", handler.Authorization);
        using JsonDocument request = JsonDocument.Parse(handler.Body);
        Assert.Equal("test-model", request.RootElement.GetProperty("model").GetString());
        JsonElement messages = request.RootElement.GetProperty("messages");
        Assert.Contains("Simplified Chinese", messages[0].GetProperty("content").GetString());
        Assert.Equal("Hello {0}\nworld", messages[1].GetProperty("content").GetString());
        Assert.Equal("json_object", request.RootElement.GetProperty("response_format").GetProperty("type").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, TranslationErrorCode.RateLimited)]
    [InlineData(HttpStatusCode.BadGateway, TranslationErrorCode.RateLimited)]
    [InlineData(HttpStatusCode.Unauthorized, TranslationErrorCode.InvalidRequest)]
    public void MapsHttpErrors(HttpStatusCode status, TranslationErrorCode expected)
    {
        var handler = new StubHandler(status, "{\"error\":{\"message\":\"request failed\"}}");
        using var client = new HttpClient(handler);
        var platform = new OpenAICompatibleTranslate("test-key", "https://relay.example/v1", "test-model", client);

        TranslationResult result = platform.Translate("English", "ChineseSimplified", "Hello");

        Assert.False(result.Success);
        Assert.Equal(expected, result.ErrorCode);
        Assert.Contains("request failed", result.RawError);
    }

    [Fact]
    public void MissingApiKeyDoesNotSendRequest()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}");
        using var client = new HttpClient(handler);
        var platform = new OpenAICompatibleTranslate("", "https://relay.example/v1", "test-model", client);

        TranslationResult result = platform.Translate("English", "ChineseSimplified", "Hello");

        Assert.Equal(TranslationErrorCode.InvalidRequest, result.ErrorCode);
        Assert.Null(handler.Url);
    }

    [Fact]
    public void RejectsChangedPlaceholders()
    {
        var handler = new StubHandler(HttpStatusCode.OK,
            "{\"choices\":[{\"message\":{\"content\":\"{\\\"decision\\\":\\\"成功\\\",\\\"translatedText\\\":\\\"你好 {1}\\\",\\\"reason\\\":\\\"\\\"}\"}}]}");
        using var client = new HttpClient(handler);
        var platform = new OpenAICompatibleTranslate("test-key", "https://relay.example/v1", "test-model", client);

        TranslationResult result = platform.Translate("English", "ChineseSimplified", "Hello {0}");

        Assert.False(result.Success);
        Assert.Equal(TranslationErrorCode.ServiceUnavailable, result.ErrorCode);
    }

    [Fact]
    public void AcceptsNotNeededDecisionWithoutWritingTranslation()
    {
        var handler = new StubHandler(HttpStatusCode.OK,
            "{\"choices\":[{\"message\":{\"content\":\"{\\\"decision\\\":\\\"不需翻译\\\",\\\"translatedText\\\":\\\"派系: Redwood Alliance\\\",\\\"reason\\\":\\\"名称和中文说明已完整\\\"}\"}}]}");
        using var client = new HttpClient(handler);
        var platform = new OpenAICompatibleTranslate("test-key", "https://relay.example/v1", "test-model", client);

        TranslationResult result = platform.Translate("English", "ChineseSimplified", "派系: Redwood Alliance");

        Assert.True(result.Success);
        Assert.Equal(TranslationDecision.NotNeeded, result.Decision);
        Assert.Equal("派系: Redwood Alliance", result.TranslatedText);
    }

    [Fact]
    public void RejectsInstructionLikePlainTextResponse()
    {
        var handler = new StubHandler(HttpStatusCode.OK,
            "{\"choices\":[{\"message\":{\"content\":\"请提供需要翻译的英文游戏界面文本\"}}]}");
        using var client = new HttpClient(handler);
        var platform = new OpenAICompatibleTranslate("test-key", "https://relay.example/v1", "test-model", client);

        TranslationResult result = platform.Translate("English", "ChineseSimplified", "Launch game");

        Assert.False(result.Success);
        Assert.Equal(TranslationDecision.Failed, result.Decision);
        Assert.Contains("JSON", result.RawError);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode status;
        private readonly string responseBody;

        public StubHandler(HttpStatusCode status, string responseBody)
        {
            this.status = status;
            this.responseBody = responseBody;
        }

        public string Url { get; private set; }
        public string Authorization { get; private set; }
        public string Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri.AbsoluteUri;
            Authorization = request.Headers.Authorization?.ToString();
            Body = await request.Content.ReadAsStringAsync();
            return new HttpResponseMessage(status) { Content = new StringContent(responseBody) };
        }
    }
}
