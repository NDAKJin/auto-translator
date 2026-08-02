using System.IO;
using Verse;

namespace nomadicooer.AutoTranslator;

/// <summary>
/// 跨文件共享的常量约定。
/// 仅存放被多个类引用、属于公开约定的常量（如 key 前缀、ModId）。
/// 单类内部专用的常量（flush 阈值、私有键等）应放在各自类内，不要放进这里。
/// </summary>
public static class AutoTranslatorConstants
{
  /// <summary>自动翻译 key 前缀。无 key 字符串自动建 key 时统一加此前缀，落盘到 auto.xml。</summary>
  public const string AutoKeyPrefix = "auto.";

  /// <summary>翻译 key 前缀。系统既有 key 的统一前缀（区别于 auto. 自动 key）。</summary>
  public const string TranslationKeyPrefix = "";

  /// <summary>本模组 ID。用于 Harmony 补丁 owner、日志标识等。</summary>
  public const string ModId = "nomadicooer.AutoTranslator";

  /// <summary>本模组在 mods 根目录下的包文件夹名（与 packageId 末尾一致）。用于定位 mod 包目录。</summary>
  public const string ModPackageDir = "AutoTranslator";

  /// <summary>自动翻译记录文件名。写入当前激活语言包的 Keyed 目录。</summary>
  public const string AutoXmlFileName = "auto.xml";

  /// <summary>纯文本日志子目录名。AutoLog 文件出口将日志写入 mod 包目录下的此子目录。</summary>
  public const string LogDirName = "Logs";

  /// <summary>纯文本日志文件名。AutoLog 文件出口的落盘文件名。</summary>
  public const string LogFileName = "autotranslator.log";

  /// <summary>本模组包根目录（mods 根目录/AutoTranslator）。运行时依赖 Unity 环境，用静态属性而非 const。</summary>
  public static string ModRootDir => Path.Combine(GenFilePaths.ModsFolderPath, ModPackageDir);
}
