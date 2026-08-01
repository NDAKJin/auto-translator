namespace nomadicooer.AutoTranslator;

/// <summary>
/// 模组基础设施常量（跨模块共享的全局标识）。
/// 业务相关的翻译键名、持久化键名等就近定义在使用它们的类中，避免单一大文件膨胀。
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

    /// <summary>
    /// 待翻译清单文件名（仅记录未翻译 key、不写译文），写入当前语言包的 Keyed 目录下。
    /// 当"实时翻译"关闭时，拦截到的 key 仅追加到此文件；玩家点击"一键翻译"后批量消费。
    /// </summary>
    public const string PendingXmlFileName = "pending.xml";
}
