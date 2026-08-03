using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml;
using UnityEngine;
using Verse;

namespace nomadicooer.AutoTranslator;

/// <summary>自动翻译数据唯一存储：内存字典为状态源，缓冲落盘到当前语言包 auto.xml。</summary>
public static class AutoTranslationStore
{
  private const int FlushThreshold = 50;                // 累积条数达此值落盘
  private const int FlushIntervalMilliseconds = 10000;  // 距上次落盘超此时长落盘（正常退出有钩子兜底，可放宽）
  private static readonly string XmlRootTag = "LanguageData";

  private static readonly Dictionary<string, TranslationEntry> store = [];
  private static int dirtyCount;                        // 自上次落盘新增/变更条数

  private static readonly object lockObj = new();       // 保护 store / dirtyCount
  private static readonly object flushLock = new();     // 保护写盘（独立于 lockObj，避免写盘阻塞所有写入口）
  private static readonly object hookLock = new();     // 保护退出钩子注册
  private static Timer pendingFlushTimer;               // 防抖一次性定时器（临时工）：有脏数据才雇，10s 后 flush 一次自动消失
  private static bool loaded;                           // 是否已从磁盘加载历史（一次性）
  private static volatile bool exitHookRegistered;      // 退出钩子是否已注册（一次性）

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
    ScheduleFlush();
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
    ScheduleFlush();
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
    if (!Writable || Prefs.DevMode)
    {
      return;
    }
    ScheduleFlush();
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
    if (!Writable || Prefs.DevMode)
    {
      return;
    }
    ScheduleFlush();
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

  /// <summary>安排一次延迟落盘（防抖一次性定时器，即 JS 的 setTimeout 模式）：有脏数据时调用，
  /// 若当前没有待触发的定时器就雇一个临时工，FlushIntervalMilliseconds 后 flush 一次自动消失；
  /// 10 秒内又来新写入则不再雇（沿用已在排程的那次），天然防抖。顺带注册退出钩子（仅一次）。</summary>
  private static void ScheduleFlush()
  {
    EnsureExitHookRegistered();
    // 已有待触发的临时工就不重复雇
    if (Interlocked.CompareExchange(ref pendingFlushTimer, null, null) != null)
    {
      return;
    }
    Timer t = new(_ =>
    {
      Flush();
      var old = Interlocked.Exchange(ref pendingFlushTimer, null);
      old?.Dispose();
    }, null, FlushIntervalMilliseconds, Timeout.Infinite);
    // 若期间已有别的线程先雇了，丢弃本次的，避免两个定时器并存
    if (Interlocked.CompareExchange(ref pendingFlushTimer, t, null) != null)
    {
      t.Dispose();
    }
  }

  /// <summary>注册退出时强制落盘钩子，仅一次。挂两处兜底，覆盖正常退出路径：
  /// ① <see cref="Application.quitting"/>：Unity 退出流程刚开始时触发（Alt+F4 / 点 X / 游戏内退出），此时主线程与 RimWorld 静态状态仍可用，是首选时机；
  /// ② <see cref="AppDomain.ProcessExit"/>：CLR 卸载前最后兜底，防止 Unity 事件因故未触发。
  /// 两处都调用 <see cref="Flush"/>，Flush 内部 dirtyCount==0 会直接返回，重复调用安全（幂等）。
  /// 注意：强制终止（taskkill /F、拔电源、蓝屏）任何钩子都不会执行，此类场景无解，由延迟落盘把损失控制在最后一个周期内。</summary>
  private static void EnsureExitHookRegistered()
  {
    if (exitHookRegistered)
    {
      return;
    }
    lock (hookLock)
    {
      if (exitHookRegistered)
      {
        return;
      }
      exitHookRegistered = true;
      try
      {
        Application.quitting += FlushOnExit;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => FlushOnExit();
      }
      catch (Exception ex)
      {
        AutoLog.Warn("注册退出落盘钩子失败", ex);
      }
    }
  }

  /// <summary>退出钩子回调：立即落盘（不依赖待触发的定时器，它可能还没到 10 秒）。
  /// 异常吞掉，避免在退出流程里抛出打断游戏关闭。</summary>
  private static void FlushOnExit()
  {
    try
    {
      var t = Interlocked.Exchange(ref pendingFlushTimer, null);
      t?.Dispose();
      Flush();
    }
    catch (Exception ex)
    {
      AutoLog.Warn("退出落盘失败", ex);
    }
  }

  /// <summary>强制落盘：全量序列化写出 auto.xml；无写条件或无可写内容时返回。
  /// 整体在 flushLock 内（含 AutoXmlFilePath 取值与写盘），避免主线程与定时器线程并发写同一文件，
  /// 也避免在非主线程访问 RimWorld 静态状态（LanguageDatabase.activeLanguage 等）时抛未捕获 NRE 导致进程崩溃。</summary>
  public static void Flush()
  {
    lock (flushLock)
    {
      try
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
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, Serialize(snapshot), Encoding.UTF8);
      }
      catch (Exception ex)
      {
        AutoLog.Warn("写入 auto.xml 失败", ex);
      }
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

  /// <summary>查询某 key 我们是否已生成译文（Translated==true）。供上层直接判断译状态，无需反向推断。</summary>
  public static bool IsTranslated(string key)
  {
    if (key == null)
    {
      return false;
    }
    lock (lockObj)
    {
      return store.TryGetValue(key, out TranslationEntry e) && e.Translated;
    }
  }

  /// <summary>取出某 key 我们已生成的译文（Translated==true 且译文非空）。供上层直接回写界面，无需重复提交。</summary>
  public static bool TryGetTranslation(string key, out string translation)
  {
    translation = null;
    if (key == null)
    {
      return false;
    }
    lock (lockObj)
    {
      if (store.TryGetValue(key, out TranslationEntry e) && e.Translated && !e.Translation.NullOrEmpty())
      {
        translation = e.Translation;
        return true;
      }
      return false;
    }
  }

  /// <summary>返回当前内存中所有未翻译（Translated=false）的条目（key, 源文），供「一键翻译」批量入队。</summary>
  public static IEnumerable<(string Key, string Source)> LoadUnTranslated()
  {
    List<(string, string)> result = new();
    lock (lockObj)
    {
      foreach (TranslationEntry e in store.Values)
      {
        if (!e.Translated && !string.IsNullOrEmpty(e.Source))
        {
          result.Add((e.Key, e.Source));
        }
      }
    }
    return result;
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
