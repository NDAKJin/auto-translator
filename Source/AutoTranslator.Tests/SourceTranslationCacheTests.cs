using nomadicooer.AutoTranslator;
using Xunit;

namespace AutoTranslator.Tests;

public class SourceTranslationCacheTests
{
    [Fact]
    public void ReusesExactSourcePerLanguageAndPrefersVerifiedTranslation()
    {
        SourceTranslationCache.Clear();
        SourceTranslationCache.Remember("zh", "Hello", "你好");
        Assert.Equal("你好", SourceTranslationCache.Remember("zh", "Hello", "您好"));
        SourceTranslationCache.Remember("zh", "Hello", "您好", true);
        Assert.True(SourceTranslationCache.TryGet("zh", "Hello", out string value));
        Assert.Equal("您好", value);
        Assert.False(SourceTranslationCache.TryGet("zh", "hello", out _));
        Assert.False(SourceTranslationCache.TryGet("ja", "Hello", out _));
        SourceTranslationCache.Clear();
        Assert.False(SourceTranslationCache.TryGet("zh", "Hello", out _));
    }
}
