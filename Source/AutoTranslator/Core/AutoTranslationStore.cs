using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml;
using Verse;

namespace nomadicooer.AutoTranslator;

/// <summary>自动翻译数据唯一存储：内存字典为状态源，缓冲落盘到当前语言包 auto.xml。</summary>
public static class AutoTranslationStore
{
  private const int FlushThreshold = 50;                // 累积条数达此值落盘
  private const int FlushIntervalMilliseconds = 3000;   // 距上次落盘超此时长落盘
  private static readonly string XmlRootTag = "LanguageData";

  private static readonly Dictionary<string, string> store = [];
  private static int dirtyCount;                        // 自上次落盘新增/变更条数

  private static readonly object lockObj = new();       // 保护 store / dirtyCount
  private static readonly object startLock = new();     // 保护定时器懒启动
  private static volatile bool timerStarted;            // 定时器是否已启动（懒启动标志）
  private static Timer flushTimer;                      // 后台线程池定时器，非主线程
  private static bool loaded;                           // 是否已从磁盘加载历史（一次性）

  // 翻译记录开关：由设置页通过 SetTranslationLogEnabled 注入，不反向依赖上层设置类
  private static bool enabledTranslationLog;

  /// <summary>注入翻译记录开关（设置项 enableTranslationLog 的镜像）。</summary>
  public static void SetTranslationLogEnabled(bool enabled)
  {
    enabledTranslationLog = enabled;
  }

  /// <summary>可写：开关开启且当前语言非 English（English 为源语言基准包，跳过）。</summary>
  private static bool Writable
  {
    get
    {
      if (!enabledTranslationLog)
      {
        return false;
      }
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
    EnsureTimerStarted();
    if (!Writable || key == null || string.IsNullOrEmpty(value))
    {
      return;
    }
    TryLoad();
    bool needFlush;
    lock (lockObj)
    {
      if (store.TryGetValue(key, out string existing) && existing == value)
      {
        return;
      }
      store[key] = value;
      dirtyCount++;
      needFlush = dirtyCount >= FlushThreshold;
    }
    if (needFlush)
    {
      Flush();
    }
  }

  /// <summary>懒启动后台定时器：首次 Record 调用时初始化，仅一次（双检锁）。</summary>
  private static void EnsureTimerStarted()
  {
    if (timerStarted)
    {
      return;
    }
    lock (startLock)
    {
      if (!timerStarted)
      {
        flushTimer = new Timer(_ => FlushIfDirty(), null, FlushIntervalMilliseconds, FlushIntervalMilliseconds);
        timerStarted = true;
      }
    }
  }

  /// <summary>定时器回调：有 dirty 才落盘，避免无写入时写空盘。</summary>
  private static void FlushIfDirty()
  {
    if (dirtyCount > 0)
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
    // 短锁拷快照后立即释放锁，再由主线程外的定时器线程写盘，避免锁住共享状态
    Dictionary<string, string> snapshot;
    lock (lockObj)
    {
      snapshot = new Dictionary<string, string>(store);
      dirtyCount = 0;
    }
    try
    {
      Directory.CreateDirectory(Path.GetDirectoryName(path));
      File.WriteAllText(path, Serialize(snapshot), Encoding.UTF8);
    }
    catch (Exception ex)
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

  /// <summary>序列化为 RimWorld Keyed 格式 xml，键按字母序稳定输出；经 XmlWriter 自动处理转义。</summary>
  private static string Serialize(Dictionary<string, string> data)
  {
    List<string> keys = [.. data.Keys];
    keys.Sort();
    XmlWriterSettings settings = new()
    {
      Encoding = Encoding.UTF8,
      Indent = true,
      IndentChars = "  ",
      OmitXmlDeclaration = false,
    };
    StringBuilder sb = new();
    using (XmlWriter writer = XmlWriter.Create(sb, settings))
    {
      writer.WriteStartDocument();
      writer.WriteStartElement(XmlRootTag);
      foreach (string key in keys)
      {
        // 跳过无法作为合法元素名的 key（如含空格/冒号），避免 XmlWriter 抛异常
        if (string.IsNullOrEmpty(key) || !XmlReader.IsName(key))
        {
          AutoLog.Warn($"跳过非法翻译 key（不可作 xml 元素名）: {key}");
          continue;
        }
        writer.WriteElementString(key, data[key]);
      }
      writer.WriteEndElement();
      writer.WriteEndDocument();
    }
    return sb.ToString();
  }

  /// <summary>首次可写时从当前语言包 auto.xml 加载历史翻译到内存；仅填 store，不触 dirtyCount 以免落盘覆盖。</summary>
  private static void TryLoad()
  {
    if (loaded)
    {
      return;
    }
    loaded = true;
    string path = AutoXmlFilePath;
    if (path == null || !File.Exists(path))
    {
      return;
    }
    try
    {
      XmlDocument doc = new();
      doc.Load(path);
      XmlElement root = doc.DocumentElement;
      if (root == null || root.Name != XmlRootTag)
      {
        return;
      }
      lock (lockObj)
      {
        foreach (XmlNode node in root.ChildNodes)
        {
          if (node is XmlElement el && !string.IsNullOrEmpty(el.Name))
          {
            store[el.Name] = el.InnerText;
          }
        }
      }
    }
    catch (Exception ex)
    {
      AutoLog.Warn($"加载 auto.xml 失败: {path}", ex);
    }
  }
}
