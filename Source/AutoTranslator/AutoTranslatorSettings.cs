using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace nomadicooer.AutoTranslator;

/// <summary>
/// 翻译平台凭据：一个平台对应一条记录（AccessKey / SecretKey）。
/// 列表形式存储，支持在设置页动态增删启用的平台。
/// </summary>
public class PlatformCredential : IExposable
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
        { "Volcengine", AutoTranslatorConstants.TranslationKeyPrefix + "Volcengine" },
        { "Aliyun", AutoTranslatorConstants.TranslationKeyPrefix + "Aliyun" },
        { "Baidu", AutoTranslatorConstants.TranslationKeyPrefix + "Baidu" }
    };
    /// <summary>
    /// 是否启用自动翻译（总开关）。启用后实时翻译并必然记录未翻译 key，
    /// 关闭后引擎停止、不再调用翻译 API、不再写新 key，
    /// 但本模组仍加载，已写入的 auto.xml 译文继续随语言包生效。
    /// </summary>
    public bool EnableAutoTranslate = true;

    /// <summary>
    /// 是否启用实时翻译。开启后拦截未翻译 key 时边调 API 边回写 auto.xml；
    /// 关闭后只把 key 记录进 pending.xml（待翻译清单），不调用 API、不回写，
    /// 待玩家在设置页点击"一键翻译"时再批量消费。总开关关闭时本开关无意义。
    /// </summary>
    public bool EnableRealtimeUpdate = true;

    /// <summary>
    /// 设置页"凭据"区域是否折叠（true=折叠隐藏所有平台凭据，false=展开）。
    /// </summary>
    public bool CredentialsCollapsed = false;

    /// <summary>
    /// 设置持久化键名：是否启用自动翻译（总开关）。
    /// </summary>
    public const string EnableAutoTranslateScribeKey = "enableAutoTranslate";

    /// <summary>
    /// 设置持久化键名：是否启用实时翻译。
    /// </summary>
    public const string EnableRealtimeUpdateScribeKey = "enableRealtimeUpdate";

    /// <summary>
    /// 设置持久化键名：凭据区域是否折叠。
    /// </summary>
    public const string CredentialsCollapsedScribeKey = "credentialsCollapsed";

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
    /// 设置页"是否启用自动翻译"复选框的翻译键名。
    /// </summary>
    public const string EnableAutoTranslateKey = AutoTranslatorConstants.TranslationKeyPrefix + "EnableAutoTranslate";

    /// <summary>
    /// 设置页"是否启用实时翻译"复选框的翻译键名。
    /// </summary>
    public const string EnableRealtimeUpdateKey = AutoTranslatorConstants.TranslationKeyPrefix + "EnableRealtimeUpdate";

    /// <summary>
    /// 设置页"一键翻译"按钮的翻译键名。
    /// </summary>
    public const string TranslateAllKey = AutoTranslatorConstants.TranslationKeyPrefix + "TranslateAll";

    /// <summary>
    /// 设置页"可用平台"列表标题的翻译键名。
    /// </summary>
    public const string PlatformListHeaderKey = AutoTranslatorConstants.TranslationKeyPrefix + "PlatformList";

    /// <summary>
    /// 设置页"凭据"表格标题的翻译键名。
    /// </summary>
    public const string CredentialTableHeaderKey = AutoTranslatorConstants.TranslationKeyPrefix + "CredentialTable";

    /// <summary>
    /// 设置页凭据表格"访问键"列头的翻译键名（三平台通用，百度 APP ID 也视作访问键）。
    /// </summary>
    public const string AccessKeyLabelKey = AutoTranslatorConstants.TranslationKeyPrefix + "AccessKey";

    /// <summary>
    /// 设置页凭据表格"密钥"列头的翻译键名（三平台通用）。
    /// </summary>
    public const string SecretKeyLabelKey = AutoTranslatorConstants.TranslationKeyPrefix + "SecretKey";

    /// <summary>
    /// 已启用/配置的翻译平台凭据列表（动态增删）。
    /// 内部字段，对外通过 <see cref="Credentials"/> 属性访问，确保永不返回 null。
    /// Scribe_Collections.Look 在读档失败时可能把引用置 null，故以属性兜底。
    /// </summary>
    private List<PlatformCredential> credentials;

    /// <summary>
    /// 已启用/配置的翻译平台凭据列表。永不为 null（读档失败时自动重建空列表）。
    /// </summary>
    public List<PlatformCredential> Credentials
        => credentials ?? (credentials = new List<PlatformCredential>());

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref EnableAutoTranslate, EnableAutoTranslateScribeKey, true);
        Scribe_Values.Look(ref EnableRealtimeUpdate, EnableRealtimeUpdateScribeKey, true);
        Scribe_Values.Look(ref CredentialsCollapsed, CredentialsCollapsedScribeKey, false);
        Scribe_Collections.Look(ref credentials, "credentials", LookMode.Deep);
        // 读档失败时 Scribe 可能把 credentials 置 null，兜底重建空列表。
        if (credentials == null)
        {
            credentials = new List<PlatformCredential>();
        }
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

    /// <summary>设置页"恢复默认设置"按钮的翻译键名。</summary>
    public const string ResetDefaultsKey = AutoTranslatorConstants.TranslationKeyPrefix + "ResetDefaults";

    /// <summary>
    /// 恢复首次安装时的默认设置：清空凭据列表，两个开关回到默认（开）。
    /// </summary>
    public void ResetToDefaults()
    {
        Credentials.Clear();
        EnableAutoTranslate = true;
        EnableRealtimeUpdate = true;
        CredentialsCollapsed = false;
    }
}
