using System.Collections.Generic;
using System.IO;
using System.Text;
using RimWorld;
using Verse;

namespace nomadicooer.AutoTranslator;

/// <summary>自动翻译数据唯一存储：内存字典为状态源，缓冲落盘到当前语言包 auto.xml。</summary>
public static class AutoTranslationStore
{
  private const int FlushThreshold = 50;          // 累积条数达此值落盘
  private const float FlushIntervalSeconds = 3f;  // 距上次落盘超此秒数落盘
  private static readonly string XmlRootTag = "LanguageData";

  private static readonly Dictionary<string, string> store = [];
  private static int dirtyCount;                   // 自上次落盘新增/变更条数
  private static float sinceLastFlush;             // 距上次落盘累计时间

  /// <summary>可写：当前语言非 English（English 为源语言基准包，跳过）。</summary>
  private static bool Writable
  {
    get
    {
      if (LanguageDatabase.activeLanguage == null
          || LanguageDatabase.activeLanguage == LanguageDatabase.defaultLanguage)
      {
        return false;
      }
      return true;
    }
  }

  /// <summary>记录一条翻译（写入口）：去重入内存，达阈值触发落盘。</summary>
  public static void Record(string key, string value)
  {
    if (!Writable || key == null)
    {
      return;
    }
    value ??= string.Empty;
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

  /// <summary>每帧由主循环调用，驱动定时落盘；无 dirty 不计数不写盘。</summary>
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

  /// <summary>强制落盘：全量序列化写出 auto.xml；无写条件或无可写内容时返回。</summary>
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
      AutoLog.Warn($"写入 auto.xml 失败: {path}", ex);
    }
  }

  /// <summary>当前激活语言包 auto.xml 路径；English 或无效时返回 null。</summary>
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
        AutoTranslatorGlobals.ModRootDir,
        "Languages",
        folder,
        "Keyed",
        AutoTranslatorGlobals.AutoXmlFileName);
    }
  }

  /// <summary>序列化为 RimWorld Keyed 格式 xml，键按字母序稳定输出。</summary>
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

  /// <summary>转义 xml 特殊字符（& < > "）。</summary>
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

  // 读功能占位（本阶段不实现）
  // public static void Load() { }
}
