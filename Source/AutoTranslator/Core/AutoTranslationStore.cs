using System.Collections.Generic;
using System.IO;
using System.Text;
using RimWorld;
using Verse;

namespace nomadicooer.AutoTranslator;

/// <summary>
/// 自动翻译数据的唯一存储/状态源（读写合一，本阶段先完成写功能）。
///
/// 职责（写）：
/// - 接收引擎/补丁层传来的 (key, value)，入内存字典（同 key 去重）。
/// - 缓冲落盘：累计达到阈值条数、或超过定时周期、或外部强制 Flush 时，才把内存字典
///   序列化写出到当前激活语言包的 auto.xml，避免每条翻译都重写整文件拖垮主线程。
///
/// 职责（读）：暂未实现。读可能依赖系统内部传递过来的数据，本阶段留 Load() 占位。
///
/// 路径：写入 Languages/&lt;当前激活语言 folderName&gt;/Keyed/auto.xml。
/// English 作为源语言基准包，不写 auto.xml（AutoXmlFilePath 在 English 下返回 null 跳过）。
///
/// 分层：位于核心层，依赖 AutoTranslatorConstants（基础层），被引擎/补丁层单向依赖，不反向引用上层。
/// </summary>
public static class AutoTranslationStore
{
  // —— 内部常量（单类专用，不进全局常量文件）——
  private const int FlushThreshold = 50;          // 内存累积条数达到此值即落盘
  private const float FlushIntervalSeconds = 3f;  // 距上次落盘超过此秒数即落盘
  private static readonly string XmlRootTag = "LanguageData";

  private static string modRootDir;                // 模组根目录（由 Init 注入，避免反向依赖 Mod 入口）
  private static readonly Dictionary<string, string> store = new();
  private static int dirtyCount;                   // 自上次落盘以来新增/变更的条数
  private static float sinceLastFlush;             // 距上次落盘累计时间

  /// <summary>是否已注入模组根目录，且当前语言不是 English（即写功能可用）。</summary>
  private static bool Writable
  {
    get
    {
      if (string.IsNullOrEmpty(modRootDir))
      {
        return false;
      }
      // English 为源语言基准包，跳过写入
      if (LanguageDatabase.activeLanguage == null
          || LanguageDatabase.activeLanguage == LanguageDatabase.defaultLanguage)
      {
        return false;
      }
      return true;
    }
  }

  /// <summary>
  /// 注入模组根目录。由启动初始化阶段调用一次，早于任何 Record/Flush。
  /// 核心层不反向依赖 Mod 入口，路径通过此注入获得。
  /// </summary>
  public static void Init(string modRootDirectory)
  {
    modRootDir = modRootDirectory;
  }

  /// <summary>
  /// 记录一条自动翻译结果（写功能入口）。
  /// 仅当记录模式开启且当前可写时生效；同 key 已存在则跳过（去重）。
  /// 入内存后按阈值触发落盘。
  /// </summary>
  /// <param name="key">key（通常已带 auto. 前缀）。</param>
  /// <param name="value">翻译结果文本。</param>
  public static void Record(string key, string value)
  {
    if (!Writable)
    {
      return;
    }
    if (key == null)
    {
      return;
    }
    value ??= string.Empty;

    // 内存去重：已存在相同 key 不重复计 dirty
    if (store.TryGetValue(key, out string existing) && existing == value)
    {
      return;
    }

    store[key] = value;
    dirtyCount++;
    if (dirtyCount >= FlushThreshold)
    {
      Flush();
    }
  }

  /// <summary>
  /// 每帧由主循环调用，驱动定时落盘。无 dirty 时不计数、不写盘。
  /// </summary>
  public static void Update()
  {
    if (!Writable || dirtyCount == 0)
    {
      return;
    }
    sinceLastFlush += RealTime.deltaTime;
    if (sinceLastFlush >= FlushIntervalSeconds)
    {
      Flush();
    }
  }

  /// <summary>
  /// 强制落盘：把内存字典全量序列化写出到 auto.xml。
  /// 外部在存档、切后台、退出时调用，确保不丢记录。
  /// 无可写条件或无可写内容时直接返回。
  /// </summary>
  public static void Flush()
  {
    if (!Writable || dirtyCount == 0)
    {
      return;
    }
    string path = AutoXmlFilePath;
    if (path == null)
    {
      return;
    }

    try
    {
      Directory.CreateDirectory(Path.GetDirectoryName(path));
      File.WriteAllText(path, Serialize(), Encoding.UTF8);
      dirtyCount = 0;
      sinceLastFlush = 0f;
    }
    catch (System.Exception ex)
    {
      // 写盘失败不应影响游戏主流程，仅记录日志。
      AutoLog.Warn($"写入 auto.xml 失败: {path}\n{ex}");
    }
  }

  /// <summary>当前激活语言包 auto.xml 的完整路径；English 或路径无效时返回 null。</summary>
  private static string AutoXmlFilePath
  {
    get
    {
      if (!Writable)
      {
        return null;
      }
      string folder = LanguageDatabase.activeLanguage.folderName;
      return Path.Combine(
        modRootDir,
        "Languages",
        folder,
        "Keyed",
        AutoTranslatorConstants.AutoXmlFileName);
    }
  }

  /// <summary>
  /// 将内存字典序列化为 RimWorld Keyed 格式 xml。
  /// 结构：&lt;LanguageData&gt;&lt;key&gt;value&lt;/key&gt;...&lt;/LanguageData&gt;
  /// 键按字母序稳定输出，便于 diff。
  /// </summary>
  private static string Serialize()
  {
    StringBuilder sb = new();
    sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
    sb.Append('<').Append(XmlRootTag).Append(">\n");
    List<string> keys = new(store.Keys);
    keys.Sort();
    foreach (string key in keys)
    {
      sb.Append("  <")
        .Append(XmlEncode(key))
        .Append('>')
        .Append(XmlEncode(store[key]))
        .Append("</")
        .Append(XmlEncode(key))
        .Append(">\n");
    }
    sb.Append("</").Append(XmlRootTag).Append('>');
    return sb.ToString();
  }

  /// <summary>转义 xml 文本中的特殊字符（&lt; &gt; &amp; 及引号）。key/value 均可能含这些字符。</summary>
  private static string XmlEncode(string s)
  {
    if (s == null)
    {
      return string.Empty;
    }
    return s.Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
  }

  // —— 读功能占位（本阶段不实现，留待后续依赖系统内部数据接入）——
  // public static void Load() { }
}
