using System;
using nomadicooer.AutoTranslator;
using Xunit;

namespace AutoTranslator.Tests;

/// <summary>
/// Obfuscation 单元测试：覆盖 Encode/Decode 往返、空值与旧明文兼容等行为。
/// 纯逻辑，不依赖 RimWorld/Unity，可在 net9.0 下独立运行。
/// </summary>
public class ObfuscationTests
{
    [Theory]
    [InlineData("my-secret-access-key")]
    [InlineData("abc123")]
    [InlineData("包含中文的密钥")]
    [InlineData("   前后空格   ")]
    public void Encode_Decode_RoundTrip_ReturnsOriginal(string plain)
    {
        string encoded = Obfuscation.Encode(plain);
        string decoded = Obfuscation.Decode(encoded);

        Assert.Equal(plain, decoded);
    }

    [Fact]
    public void Encode_DoesNotLeakPlaintext()
    {
        const string plain = "super-secret-token";
        string encoded = Obfuscation.Encode(plain);

        Assert.NotEqual(plain, encoded);
        Assert.DoesNotContain("super-secret", encoded);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Decode_EmptyOrNull_ReturnsEmptyString(string encoded)
    {
        string decoded = Obfuscation.Decode(encoded);

        Assert.Equal(string.Empty, decoded);
    }

    [Fact]
    public void Encode_EmptyString_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, Obfuscation.Encode(string.Empty));
    }

    [Fact]
    public void Decode_LegacyPlaintext_ReturnsOriginal()
    {
        // 旧版存档中可能是未混淆的明文（非 Base64），应原样返回而非抛异常。
        const string legacy = "plaintext-not-base64!";
        string decoded = Obfuscation.Decode(legacy);

        Assert.Equal(legacy, decoded);
    }
}
