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
    /// 设置持久化键名：是否启用翻译键记录。
    /// </summary>
    public const string EnableKeyRecordingScribeKey = "enableKeyRecording";

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
    /// 火山引擎 AccessKey，从设置界面输入，持久化保存。
    /// </summary>
    public string VolcengineAccessKey = string.Empty;

    /// <summary>
    /// 火山引擎 SecretKey，从设置界面输入，持久化保存。
    /// </summary>
    public string VolcengineSecretKey = string.Empty;

    /// <summary>
    /// 设置持久化键名：火山引擎 AccessKey。
    /// </summary>
    public const string VolcengineAccessKeyScribeKey = "volcengineAccessKey";

    /// <summary>
    /// 设置持久化键名：火山引擎 SecretKey。
    /// </summary>
    public const string VolcengineSecretKeyScribeKey = "volcengineSecretKey";

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref EnableKeyRecording, EnableKeyRecordingScribeKey, true);
        Scribe_Values.Look(ref VolcengineAccessKey, VolcengineAccessKeyScribeKey, string.Empty);
        Scribe_Values.Look(ref VolcengineSecretKey, VolcengineSecretKeyScribeKey, string.Empty);
    }
}
