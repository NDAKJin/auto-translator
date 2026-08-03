using System.Text;

namespace nomadicooer.AutoTranslator;

/// <summary>轻量混淆：防明文一眼可读，非真加密。仅用于本地存档的 API key。</summary>
public static class Obfuscation
{
    // 固定盐，编译期常量，不进存档。
    private const string Salt = "at_obf_2026";

    /// <summary>混淆明文（异或盐 + Base64）。空值原样返回。</summary>
    public static string Encode(string plain)
    {
        if (string.IsNullOrEmpty(plain))
        {
            return plain;
        }
        byte[] bytes = Encoding.UTF8.GetBytes(plain);
        byte[] salt = Encoding.UTF8.GetBytes(Salt);
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] ^= salt[i % salt.Length];
        }
        return System.Convert.ToBase64String(bytes);
    }

    /// <summary>还原混淆串。失败返回原串（兼容旧明文存档）。</summary>
    public static string Decode(string encoded)
    {
        if (string.IsNullOrEmpty(encoded))
        {
            return encoded;
        }
        try
        {
            byte[] bytes = System.Convert.FromBase64String(encoded);
            byte[] salt = Encoding.UTF8.GetBytes(Salt);
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] ^= salt[i % salt.Length];
            }
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return encoded;
        }
    }

    /// <summary>显示打码：留首尾各 2 位，中间以 **** 代替；过短则全 *。仅用于设置页展示。</summary>
    public static string Mask(string plain, int visibleEachSide = 2)
    {
        if (string.IsNullOrEmpty(plain))
        {
            return plain;
        }
        if (plain.Length <= visibleEachSide * 2)
        {
            return new string('*', plain.Length);
        }
        return plain.Substring(0, visibleEachSide)
            + "****"
            + plain.Substring(plain.Length - visibleEachSide);
    }

    /// <summary>判断字符串是否仍为打码形态（含 ****），用于设置页区分"用户已编辑"。</summary>
    public static bool IsMasked(string text) => !string.IsNullOrEmpty(text) && text.Contains("****");
}
