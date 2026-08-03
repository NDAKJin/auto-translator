using System.Collections.Generic;

namespace nomadicooer.AutoTranslator;

/// <summary>
/// 平台清单注册表。各翻译引擎类通过公共常量 <c>PlatformNameKey</c>
/// （如 "AutoTranslator.Volcengine"）声明自身标志，此处统一聚合为设置页与启动派发使用的清单。
/// PlatformNameKey 是引擎的唯一标志，任何需要平台标识的地方都引用它，避免字符串字面量散落重复。
/// </summary>
public static class PlatformRegistry
{
    /// <summary>
    /// 所有平台的 PlatformNameKey（带前缀的完整标志），顺序即设置页列表顺序。
    /// 新增平台只需在对应引擎类定义 PlatformNameKey，此处追加一行即可。
    /// </summary>
    public static readonly IReadOnlyList<string> AllPlatformNameKeys =
    [
        VolcengineTranslate.PlatformNameKey,
        AliyunTranslate.PlatformNameKey,
        BaiduTranslate.PlatformNameKey
    ];

    /// <summary>
    /// 平台标志（PlatformNameKey，带前缀）→ 设置页显示用的翻译键名。
    /// 当前 PlatformNameKey 本身即翻译键名，二者一致。
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> LabelKeys = new Dictionary<string, string>
    {
        { VolcengineTranslate.PlatformNameKey, VolcengineTranslate.PlatformNameKey },
        { AliyunTranslate.PlatformNameKey, AliyunTranslate.PlatformNameKey },
        { BaiduTranslate.PlatformNameKey, BaiduTranslate.PlatformNameKey }
    };

    /// <summary>
    /// 平台标志（PlatformNameKey，带前缀）→ 平台类型。供引擎用 Activator 反射构造成平台实例。
    /// 在各引擎类声明 PlatformNameKey 常量后，此处追加一行类型映射即可。
    /// </summary>
    public static readonly IReadOnlyDictionary<string, System.Type> PlatformTypes = new Dictionary<string, System.Type>
    {
        { VolcengineTranslate.PlatformNameKey, typeof(VolcengineTranslate) },
        { AliyunTranslate.PlatformNameKey, typeof(AliyunTranslate) },
        { BaiduTranslate.PlatformNameKey, typeof(BaiduTranslate) }
    };
}
