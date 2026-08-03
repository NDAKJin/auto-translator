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

  private static readonly Dictionary<string, TranslationEntry> store = [];
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

  /// <summary>写入口一：记录源文（原文）。原文缺失（source 为空）不入库；原文不得用 key 顶替。
  /// 无该 key 时新建（译文回退为原文、Translated=false）；已有则仅在原文本空时补源文。</summary>
  public static void RecordSource(string key, string source)
  {
    if (key == null || string.IsNullOrEmpty(source))
    {
      return;
    }
    if (!Writable || Prefs.DevMode)
    {
      return;
    }
    EnsureTimerStarted();
    TryLoad();
    Mutate(key, existing =>
    {
      // 已有源文则沿用，不覆盖；原文缺失才补
      string mergedSource = string.IsNullOrEmpty(existing?.Source) ? source : existing.Source;
      string mergedTranslation = existing?.Translation;
      if (string.IsNullOrEmpty(mergedTranslation))
      {
        mergedTranslation = mergedSource;
      }
      bool mergedTranslated = existing?.Translated ?? false;
      return new TranslationEntry(key, mergedSource, mergedTranslation, mergedTranslated);
    });
  }



  /// <summary>写入口二：记录译文。必须先有原文（之前未存入原文的打回不入库）；
  /// 译文为空不入库。Translated 置 true。</summary>
  public static void RecordTranslation(string key, string translation)
  {
    if (key == null || string.IsNullOrEmpty(translation))
    {
      return;
    }
    if (!Writable || Prefs.DevMode)
    {
      return;
    }
    EnsureTimerStarted();
    TryLoad();
    Mutate(key, existing =>
    {
      // 译文阶段必须先有原文：无 existing 或其 Source 为空则打回
      if (existing == null || string.IsNullOrEmpty(existing.Source))
      {
        return null;
      }
      return new TranslationEntry(key, existing.Source, translation, true);
    });
  }

  /// <summary>带锁执行一次合并：从 store 取 existing，调用 merge 生成新条目；
  /// merge 返回 null 表示打回（不入库）；与现有相等则跳过；否则写入并累计 dirty 触发落盘。
  /// 人工已校验/改过的条目（Verified=true）自动路径整体跳过，保留人工成果。</summary>
  private static void Mutate(string key, Func<TranslationEntry, TranslationEntry> merge)
  {
    bool needFlush = false;
    lock (lockObj)
    {
      store.TryGetValue(key, out TranslationEntry existing);
      if (existing != null && existing.Verified)
      {
        return;
      }
      TranslationEntry updated = merge(existing);
      if (updated == null)
      {
        return;
      }
      if (existing != null && existing.Equals(updated))
      {
        return;
      }
      store[key] = updated;
      dirtyCount++;
      needFlush = dirtyCount >= FlushThreshold;
    }
    if (needFlush)
    {
      Flush();
    }
  }

  /// <summary>用户主动操作入口：在设置界面修改某条译文（人工更改），标记 Verified=true 保护，不被自动 Record 覆盖。</summary>
  public static void SetTranslation(string key, string translation)
  {
    if (key == null)
    {
      return;
    }
    TryLoad();
    bool needFlush;
    lock (lockObj)
    {
      store.TryGetValue(key, out TranslationEntry existing);
      // 原文缺失（无已有源文）则不入库：用户改译文必须以已有原文为基础
      if (string.IsNullOrEmpty(existing?.Source))
      {
        return;
      }
      TranslationEntry updated = new(key, existing.Source, translation, true, verified: true);
      if (existing != null && existing.Equals(updated))
      {
        return;
      }
      store[key] = updated;
      dirtyCount++;
      needFlush = dirtyCount >= FlushThreshold;
    }
    if (needFlush)
    {
      Flush();
    }
  }

  /// <summary>用户主动操作入口：切换某条校验状态（操作列「校验」按钮可开可关）。
  /// verified=true 保护不被自动 Record 覆盖；verified=false 恢复自动路径可覆盖。</summary>
  public static void SetVerified(string key, bool verified)
  {
    if (key == null)
    {
      return;
    }
    TryLoad();
    bool needFlush;
    lock (lockObj)
    {
      store.TryGetValue(key, out TranslationEntry existing);
      if (existing == null)
      {
        return;
      }
      if (existing.Verified == verified)
      {
        return;
      }
      TranslationEntry updated = new(key, existing.Source, existing.Translation, existing.Translated, verified);
      store[key] = updated;
      dirtyCount++;
      needFlush = dirtyCount >= FlushThreshold;
    }
    if (needFlush)
    {
      Flush();
    }
  }

  /// <summary>懒启动后台定时器：首次写入口（RecordSource/RecordTranslation）调用时初始化，仅一次（双检锁）。</summary>
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

  /// <summary>定时器回调：有 dirty 才落盘；连续无写入时自杀——停掉并释放定时器、复位 timerStarted，
  /// 让下次写入口能重新懒启动，避免空闲时一直占用线程池空转。</summary>
  private static void FlushIfDirty()
  {
    if (dirtyCount > 0)
    {
      Flush();
      return;
    }
    // 无脏数据：停掉自身定时器（自杀），复位标志以便重新懒启动
    lock (startLock)
    {
      if (timerStarted)
      {
        flushTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        flushTimer?.Dispose();
        flushTimer = null;
        timerStarted = false;
      }
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
    Dictionary<string, TranslationEntry> snapshot;
    lock (lockObj)
    {
      snapshot = new Dictionary<string, TranslationEntry>(store);
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
  private static string Serialize(Dictionary<string, TranslationEntry> data)
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
        TranslationEntry entry = data[key];
        writer.WriteStartElement(key);
        writer.WriteAttributeString("source", entry.Source);
        writer.WriteAttributeString("translated", entry.Translated ? "true" : "false");
        writer.WriteAttributeString("verified", entry.Verified ? "true" : "false");
        if (!string.IsNullOrEmpty(entry.Translation))
        {
          writer.WriteString(entry.Translation);
        }
        writer.WriteEndElement();
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
            // 原文缺失（旧文件无 source 或为空）的条目不载入：原文不得用 key 顶替
            string src = el.GetAttribute("source");
            if (string.IsNullOrEmpty(src))
            {
              AutoLog.Warn($"跳过无原文条目（source 为空）: {el.Name}");
              continue;
            }
            bool translated = true;
            string translatedAttr = el.GetAttribute("translated");
            if (!string.IsNullOrEmpty(translatedAttr)
                && bool.TryParse(translatedAttr, out bool parsed))
            {
              translated = parsed;
            }
            bool verified = false;
            string verifiedAttr = el.GetAttribute("verified");
            if (!string.IsNullOrEmpty(verifiedAttr)
                && bool.TryParse(verifiedAttr, out bool verifiedParsed))
            {
              verified = verifiedParsed;
            }
            // 旧 auto.xml 无 translated 属性时按 true 兼容；译文取 InnerText（可能为空=仅源文）
            store[el.Name] = new TranslationEntry(el.Name, src, el.InnerText, translated, verified);
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
