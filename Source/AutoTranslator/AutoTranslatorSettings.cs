using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace nomadicooer.AutoTranslator;

/// <summary>
/// 翻译平台凭据：一个平台对应一条记录（AccessKey / SecretKey）。
/// 列表形式存储，支持在设置页动态增删启用的平台。
/// </summary>
public class PlatformCredential
{
    /// <summary>平台标识：Volcengine / Aliyun / Baidu。</summary>
    public string PlatformId;

    /// <summary>访问键（阿里云 AccessKeyId / 百度 APP ID / 火山 AccessKey）。</summary>
    public string AccessKey;

    /// <summary>密钥（各平台 SecretKey）。</summary>
    public string SecretKey;

    /// <summary>RimWorld 序列化入口，元素级持久化。</summary>
    public void ExposeData()
    {
        Scribe_Values.Look(ref PlatformId, "platformId");
        Scribe_Values.Look(ref AccessKey, "accessKey");
        Scribe_Values.Look(ref SecretKey, "secretKey");
    }
}

/// <summary>
/// 模组设置。当前语言与默认语言为只读显示，不持久化；
/// 翻译平台凭据以列表形式保存，支持在设置页启用/配置多个平台。
/// </summary>
public class AutoTranslatorSettings : ModSettings
{
    /// <summary>支持的翻译平台标识集合（顺序即为设置页列表顺序）。</summary>
    public static readonly IReadOnlyList<string> PlatformIds = new[]
    {
        "Volcengine",
        "Aliyun",
        "Baidu"
    };

    /// <summary>各平台在设置页显示的翻译键名（key 为 PlatformId）。</summary>
    public static readonly IReadOnlyDictionary<string, string> PlatformLabelKeys = new Dictionary<string, string>
    {
        { "Volcengine", AutoTranslatorConstants.TranslationKeyPrefix + "Platform.Volcengine" },
        { "Aliyun", AutoTranslatorConstants.TranslationKeyPrefix + "Platform.Aliyun" },
        { "Baidu", AutoTranslatorConstants.TranslationKeyPrefix + "Platform.Baidu" }
    };
    /// <summary>
    /// 是否在翻译失败时记录未翻译的 key 到当前语言包的 auto.xml。
    /// </summary>
    public bool EnableKeyRecording = true;

    /// <summary>
    /// 是否实时翻译：开启时翻译失败立即写入 auto.xml；
    /// 关闭时仅回退显示、不记录未翻译 key。
    /// </summary>
    public bool EnableRealtimeUpdate = true;

    /// <summary>
    /// 设置持久化键名：是否启用翻译键记录。
    /// </summary>
    public const string EnableKeyRecordingScribeKey = "enableKeyRecording";

    /// <summary>
    /// 设置持久化键名：是否实时更新未翻译字串。
    /// </summary>
    public const string EnableRealtimeUpdateScribeKey = "enableRealtimeUpdate";

    /// <summary>
    /// 设置页分类的翻译键名（SettingsCategory）。
    /// </summary>
    public const string SettingsCategoryKey = AutoTranslatorConstants.TranslationKeyPrefix + "SettingsCategory";

    /// <summary>
    /// 设置页"当前语言"标签的翻译键名。
    /// </summary>
    public const string ActiveLanguageKey = AutoTranslatorConstants.TranslationKeyPrefix + "ActiveLanguage";

    /// <summary>
    /// 设置页"默认语言"标签的翻译键名。
    /// </summary>
    public const string DefaultLanguageKey = AutoTranslatorConstants.TranslationKeyPrefix + "DefaultLanguage";

    /// <summary>
    /// 设置页"是否启用翻译键记录"复选框的翻译键名。
    /// </summary>
    public const string EnableKeyRecordingKey = AutoTranslatorConstants.TranslationKeyPrefix + "EnableKeyRecording";

    /// <summary>
    /// 设置页"是否实时翻译"复选框的翻译键名。
    /// </summary>
    public const string EnableRealtimeUpdateKey = AutoTranslatorConstants.TranslationKeyPrefix + "EnableRealtimeUpdate";

    /// <summary>
    /// 设置页"可用平台"列表标题的翻译键名。
    /// </summary>
    public const string PlatformListHeaderKey = AutoTranslatorConstants.TranslationKeyPrefix + "PlatformList";

    /// <summary>
    /// 设置页"凭据"表格标题的翻译键名。
    /// </summary>
    public const string CredentialTableHeaderKey = AutoTranslatorConstants.TranslationKeyPrefix + "CredentialTable";

    /// <summary>
    /// 设置页凭据表格"访问键"列头的翻译键名。
    /// </summary>
    public const string AccessKeyLabelKey = AutoTranslatorConstants.TranslationKeyPrefix + "AccessKey";

    /// <summary>
    /// 设置页凭据表格"密钥"列头的翻译键名。
    /// </summary>
    public const string SecretKeyLabelKey = AutoTranslatorConstants.TranslationKeyPrefix + "SecretKey";

    /// <summary>
    /// 已启用/配置的翻译平台凭据列表（动态增删）。
    /// </summary>
    public List<PlatformCredential> Credentials = new List<PlatformCredential>();

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref EnableKeyRecording, EnableKeyRecordingScribeKey, true);
        Scribe_Values.Look(ref EnableRealtimeUpdate, EnableRealtimeUpdateScribeKey, true);
        Scribe_Collections.Look(ref Credentials, "credentials", LookMode.Deep);
    }

    /// <summary>是否已启用指定平台。</summary>
    public bool HasPlatform(string platformId)
    {
        return Credentials.Any(c => c.PlatformId == platformId);
    }

    /// <summary>获取指定平台的凭据（不存在返回 null）。</summary>
    public PlatformCredential GetCredential(string platformId)
    {
        return Credentials.FirstOrDefault(c => c.PlatformId == platformId);
    }

    /// <summary>启用指定平台（若尚未存在则追加一条空记录）。</summary>
    public void EnablePlatform(string platformId)
    {
        if (!HasPlatform(platformId))
        {
            Credentials.Add(new PlatformCredential { PlatformId = platformId });
        }
    }

    /// <summary>停用指定平台（移除其凭据记录）。</summary>
    public void DisablePlatform(string platformId)
    {
        Credentials.RemoveAll(c => c.PlatformId == platformId);
    }
}
