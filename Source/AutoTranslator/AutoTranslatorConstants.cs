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
    /// 单文件承担两种角色：实时翻译开启时 value=译文（translated="true"）；
    /// 关闭时 value=默认语言（英文）原文内容（translated="false"）。
    /// "一键翻译"只挑 translated="false" 的条目提交，翻译成功后改写为 translated="true"，
    /// 不依赖"value 是否等于原文"推断，杜绝引擎返回原文造成的死循环。
    /// </summary>
    public const string AutoXmlFileName = "auto.xml";
}
