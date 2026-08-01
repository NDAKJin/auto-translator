using RimWorld;
using Verse;

namespace nomadicooer.AutoTranslator;

/// <summary>
/// 模组设置。当前语言与默认语言为只读显示，不持久化；
/// 仅"是否启用翻译键记录"需要保存。
/// </summary>
public class AutoTranslatorSettings : ModSettings
{
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
    /// 设置页"火山引擎 AccessKey"文本框的翻译键名。
    /// </summary>
    public const string VolcengineAccessKeyLabelKey = AutoTranslatorConstants.TranslationKeyPrefix + "VolcengineAccessKey";

    /// <summary>
    /// 设置页"火山引擎 SecretKey"文本框的翻译键名。
    /// </summary>
    public const string VolcengineSecretKeyLabelKey = AutoTranslatorConstants.TranslationKeyPrefix + "VolcengineSecretKey";

    /// <summary>
    /// 设置页"阿里云 AccessKeyId"文本框的翻译键名。
    /// </summary>
    public const string AliyunAccessKeyIdLabelKey = AutoTranslatorConstants.TranslationKeyPrefix + "AliyunAccessKeyId";

    /// <summary>
    /// 设置页"阿里云 AccessKeySecret"文本框的翻译键名。
    /// </summary>
    public const string AliyunAccessKeySecretLabelKey = AutoTranslatorConstants.TranslationKeyPrefix + "AliyunAccessKeySecret";

    /// <summary>
    /// 设置页"百度 APP ID"文本框的翻译键名。
    /// </summary>
    public const string BaiduAppIdLabelKey = AutoTranslatorConstants.TranslationKeyPrefix + "BaiduAppId";

    /// <summary>
    /// 设置页"百度密钥"文本框的翻译键名。
    /// </summary>
    public const string BaiduSecretKeyLabelKey = AutoTranslatorConstants.TranslationKeyPrefix + "BaiduSecretKey";

    /// <summary>
    /// 火山引擎 AccessKey，从设置界面输入，持久化保存。
    /// </summary>
    public string VolcengineAccessKey = string.Empty;

    /// <summary>
    /// 火山引擎 SecretKey，从设置界面输入，持久化保存。
    /// </summary>
    public string VolcengineSecretKey = string.Empty;

    /// <summary>
    /// 阿里云 AccessKeyId，从设置界面输入，持久化保存。
    /// </summary>
    public string AliyunAccessKeyId = string.Empty;

    /// <summary>
    /// 阿里云 AccessKeySecret，从设置界面输入，持久化保存。
    /// </summary>
    public string AliyunAccessKeySecret = string.Empty;

    /// <summary>
    /// 百度翻译 APP ID，从设置界面输入，持久化保存。
    /// </summary>
    public string BaiduAppId = string.Empty;

    /// <summary>
    /// 百度翻译密钥，从设置界面输入，持久化保存。
    /// </summary>
    public string BaiduSecretKey = string.Empty;

    /// <summary>
    /// 设置持久化键名：火山引擎 AccessKey。
    /// </summary>
    public const string VolcengineAccessKeyScribeKey = "volcengineAccessKey";

    /// <summary>
    /// 设置持久化键名：火山引擎 SecretKey。
    /// </summary>
    public const string VolcengineSecretKeyScribeKey = "volcengineSecretKey";

    /// <summary>
    /// 设置持久化键名：阿里云 AccessKeyId。
    /// </summary>
    public const string AliyunAccessKeyIdScribeKey = "aliyunAccessKeyId";

    /// <summary>
    /// 设置持久化键名：阿里云 AccessKeySecret。
    /// </summary>
    public const string AliyunAccessKeySecretScribeKey = "aliyunAccessKeySecret";

    /// <summary>
    /// 设置持久化键名：百度翻译 APP ID。
    /// </summary>
    public const string BaiduAppIdScribeKey = "baiduAppId";

    /// <summary>
    /// 设置持久化键名：百度翻译密钥。
    /// </summary>
    public const string BaiduSecretKeyScribeKey = "baiduSecretKey";

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref EnableKeyRecording, EnableKeyRecordingScribeKey, true);
        Scribe_Values.Look(ref EnableRealtimeUpdate, EnableRealtimeUpdateScribeKey, true);
        Scribe_Values.Look(ref VolcengineAccessKey, VolcengineAccessKeyScribeKey, string.Empty);
        Scribe_Values.Look(ref VolcengineSecretKey, VolcengineSecretKeyScribeKey, string.Empty);
        Scribe_Values.Look(ref AliyunAccessKeyId, AliyunAccessKeyIdScribeKey, string.Empty);
        Scribe_Values.Look(ref AliyunAccessKeySecret, AliyunAccessKeySecretScribeKey, string.Empty);
        Scribe_Values.Look(ref BaiduAppId, BaiduAppIdScribeKey, string.Empty);
        Scribe_Values.Look(ref BaiduSecretKey, BaiduSecretKeyScribeKey, string.Empty);
    }
}
