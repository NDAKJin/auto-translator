using nomadicooer.AutoTranslator;
using Xunit;

namespace AutoTranslator.Tests;

public class VisibleTextFilterTests
{
    [Theory]
    [InlineData("Redwood Alliance", "ChineseSimplified", true)]
    [InlineData("\u6d3e\u7cfb\u003a Redwood Alliance", "ChineseSimplified", true)]
    [InlineData("RimWorld\u7684\u6240\u6709\u7ffb\u8bd1\u5747\u7531\u672c\u6e38\u620f\u7684\u7231\u597d\u8005\u4eec\u5236\u4f5c\u3002\n\u7ffb\u8bd1\u6210\u5458\u5982\u4e0b\uff1a\nAlyxMS\nBigmap", "ChineseSimplified", true)]
    [InlineData("Harmony v2.4.3.0", "ChineseSimplified", false)]
    [InlineData("[14:59:48] Combat Extended :: Checking Multiplayer Compat", "ChineseSimplified", false)]
    [InlineData("(2) [14:58:04] Config error in mod", "ChineseSimplified", false)]
    [InlineData("at System.Net.Http.HttpClient.SendAsync ()", "ChineseSimplified", false)]
    [InlineData(@"D:\\Games\\RimWorld\\Mods\\About.xml", "ChineseSimplified", false)]
    [InlineData("Things/Mote/Explosion.png", "ChineseSimplified", false)]
    [InlineData("[12:30] The colony is under attack", "ChineseSimplified", true)]
    [InlineData("The file is missing from the colony", "ChineseSimplified", true)]
    [InlineData("<color=red>New faction</color>", "ChineseSimplified", true)]
    [InlineData("https://example.com", "ChineseSimplified", false)]
    [InlineData("123", "ChineseSimplified", false)]
    [InlineData("\u0421\u043e\u044e\u0437", "ChineseSimplified", true)]
    [InlineData("\u041c\u0438\u0440", "Russian", false)]
    public void DetectsVisibleForeignText(string text, string target, bool expected)
    {
        Assert.Equal(expected, VisibleTextFilter.ShouldTranslate(text, target));
    }

    [Theory]
    [InlineData("Redwood Alliance", "English")]
    [InlineData("\u0421\u043e\u044e\u0437", "Russian")]
    [InlineData("\u30c6\u30b9\u30c8", "Japanese")]
    [InlineData("\ud55c\uad6d", "Korean")]
    public void InfersCommonSourceScript(string text, string expected)
    {
        Assert.Equal(expected, VisibleTextFilter.GuessSource(text));
    }
}
