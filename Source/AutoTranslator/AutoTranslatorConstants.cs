namespace nomadicooer.AutoTranslator;

/// <summary>
/// 模组全局常量。命名空间、packageId、Harmony owner id 三者统一为同一个字符串。
/// </summary>
public static class AutoTranslatorConstants
{
    /// <summary>
    /// 模组 ID，同时作为 C# 命名空间、About.xml 的 &lt;packageId&gt; 与 Harmony 补丁 owner id。
    /// </summary>
    public const string ModId = "nomadicooer.AutoTranslator";

    /// <summary>
    /// 翻译键前缀。所有 Keyed 翻译键均以该前缀开头（对应 AutoTranslator.xml 中的键名）。
    /// </summary>
    public const string TranslationKeyPrefix = "AutoTranslator.";

    /// <summary>
    /// 自动记录未翻译 key 时生成的翻译文件名，写入当前语言包的 Keyed 目录下。
    /// </summary>
    public const string AutoXmlFileName = "auto.xml";
}
