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
}
