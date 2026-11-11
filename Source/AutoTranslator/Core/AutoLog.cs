using System;
using System.Diagnostics;
using System.IO;

namespace nomadicooer.AutoTranslator;

/// <summary>统一日志层：集中所有日志出口，分级 + 全局/调试开关 + 可选纯文本文件出口。
/// 内部对 Verse.Log 的依赖仅在 RIMWORLD 编译符号下生效，否则降级到 Debug.WriteLine，
/// 使翻译层在测试工程中无需引用 Assembly-CSharp 即可独立编译。</summary>
public static class AutoLog
{
  /// <summary>全局开关：false 时所有日志均不输出。默认 true。</summary>
  public static bool Enabled { get; set; } = true;

  /// <summary>调试开关：仅控制 Debug 级。默认 false。</summary>
  public static bool DebugEnabled { get; set; }

  /// <summary>文件出口开关：true 时每条日志同步 append 到 mod 包目录 Logs/。默认 true（联调期强制落盘，便于无日志可观测时定位）。</summary>
  public static bool EnabledToFile { get; set; } = true;

  /// <summary>异常摘要开关：true 只输出「类型: 消息」，false 输出完整堆栈。默认 true。</summary>
  public static bool CompactException { get; set; } = true;

  // 不依赖 AutoTranslatorGlobals（其依赖 Verse），避免测试工程编译时引入 Verse。
  private static string Prefix => "[ndakjin.autotranslator] ";

  /// <summary>异常描述：按 CompactException 决定输出摘要或全堆栈。</summary>
  private static string Describe(Exception ex)
  {
    if (ex == null)
    {
      return string.Empty;
    }
    if (!CompactException)
    {
      return ex.ToString();
    }
    Exception root = ex;
    while (root.InnerException != null)
    {
      root = root.InnerException;
    }
    return root == ex
      ? $"{ex.GetType().Name}: {ex.Message}"
      : $"{ex.GetType().Name}: {ex.Message} <- {root.GetType().Name}: {root.Message}";
  }

  /// <summary>日志时间戳（文件出口用）。</summary>
  private static string Now => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

  /// <summary>纯文本日志文件完整路径（mod 包目录/Logs/autotranslator.log）。仅 RIMWORLD 下可用。</summary>
#if RIMWORLD
  private static string LogFilePath =>
    Path.Combine(AutoTranslatorGlobals.ModRootDir, AutoTranslatorGlobals.LogDirName, AutoTranslatorGlobals.LogFileName);
#else
  private static string LogFilePath => null;
#endif

  /// <summary>追加一行到日志文件，失败静默忽略（不影响主流程、不回写日志）。</summary>
  private static void AppendToFile(string line)
  {
#if RIMWORLD
    try
    {
      var path = LogFilePath;
      var dir = Path.GetDirectoryName(path);
      if (!string.IsNullOrEmpty(dir))
      {
        Directory.CreateDirectory(dir);
      }
      File.AppendAllText(path, line + Environment.NewLine);
    }
    catch (Exception)
    {
    }
#endif
  }

  // 主线程 ID：由 AutoLog 首次在主线程被调用时（启动初始化阶段）记录。
  private static int mainThreadId = -1;

  /// <summary>由启动初始化在主线程调用，登记主线程 ID，供后台线程日志降级判定。</summary>
  public static void MarkMainThread()
  {
    mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
  }

  /// <summary>当前是否运行在主线程。未登记时保守视为主线程。</summary>
  private static bool OnMainThread =>
    mainThreadId < 0 || System.Threading.Thread.CurrentThread.ManagedThreadId == mainThreadId;

  /// <summary>统一出口：RIMWORLD 下走 Verse.Log，否则降级 Debug.WriteLine。
  /// 非主线程时绝不调用 Verse.Log（其内部加锁且非线程安全，后台并发调用会拖垮甚至死锁主线程），
  /// 此时仅由调用方落文件。</summary>
  private static void Emit(string level, string full, bool once)
  {
#if RIMWORLD
    if (!OnMainThread)
    {
      return;
    }
    if (level == "Warn")
    {
      if (once) Verse.Log.WarningOnce(full, full.GetHashCode());
      else Verse.Log.Warning(full);
    }
    else if (level == "Error")
    {
      if (once) Verse.Log.ErrorOnce(full, full.GetHashCode());
      else Verse.Log.Error(full);
    }
    else
    {
      Verse.Log.Message(full);
    }
#else
    System.Diagnostics.Debug.WriteLine($"[{level}] {full}");
#endif
  }

  /// <summary>调试级：开发期排错，受 DebugEnabled 控制。</summary>
  public static void Debug(string text)
  {
    if (!Enabled || !DebugEnabled)
    {
      return;
    }
    var full = Prefix + text;
    Emit("Info", full, false);
    if (EnabledToFile)
    {
      AppendToFile($"[{Now}] DEBUG {full}");
    }
  }

  /// <summary>信息级：常规流程提示。</summary>
  public static void Info(string text)
  {
    if (!Enabled)
    {
      return;
    }
    var full = Prefix + text;
    Emit("Info", full, false);
    if (EnabledToFile)
    {
      AppendToFile($"[{Now}] INFO {full}");
    }
  }

  /// <summary>警告级：可恢复的非预期情况，每次均输出。</summary>
  public static void Warn(string text)
  {
    if (!Enabled)
    {
      return;
    }
    var full = Prefix + text;
    Emit("Warn", full, false);
    if (EnabledToFile)
    {
      AppendToFile($"[{Now}] WARN {full}");
    }
  }

  /// <summary>警告级（带异常）：异常按 CompactException 摘要或展开。</summary>
  public static void Warn(string text, Exception ex)
  {
    if (!Enabled)
    {
      return;
    }
    Warn($"{text} | {Describe(ex)}");
  }

  /// <summary>警告级（单次）：相同文本只输出一次。</summary>
  public static void WarnOnce(string text)
  {
    if (!Enabled)
    {
      return;
    }
    var full = Prefix + text;
    Emit("Warn", full, true);
    if (EnabledToFile)
    {
      AppendToFile($"[{Now}] WARN {full}");
    }
  }

  /// <summary>错误级：严重影响功能的问题，每次均输出。</summary>
  public static void Error(string text)
  {
    if (!Enabled)
    {
      return;
    }
    var full = Prefix + text;
    Emit("Error", full, false);
    if (EnabledToFile)
    {
      AppendToFile($"[{Now}] ERROR {full}");
    }
  }

  /// <summary>错误级（带异常）：异常按 CompactException 摘要或展开。</summary>
  public static void Error(string text, Exception ex)
  {
    if (!Enabled)
    {
      return;
    }
    Error($"{text} | {Describe(ex)}");
  }

  /// <summary>错误级（单次）：相同文本只输出一次。</summary>
  public static void ErrorOnce(string text)
  {
    if (!Enabled)
    {
      return;
    }
    var full = Prefix + text;
    Emit("Error", full, true);
    if (EnabledToFile)
    {
      AppendToFile($"[{Now}] ERROR {full}");
    }
  }
}
