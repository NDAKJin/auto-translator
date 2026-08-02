using System;
using Verse;

namespace nomadicooer.AutoTranslator;

/// <summary>
/// 统一的日志包装层。
///
/// 动机：项目日志调用分散、无统一开关，无法按需开关或分级。本类集中所有日志出口，
/// 上游统一调用 AutoLog.* 而非直接用 Verse.Log.*。
///
/// 能力：
/// - 全局开关 <see cref="Enabled"/>：关闭后所有日志（含 Error）均不输出。
/// - 分级：Debug / Info / Warn / Error。Debug 级受 <see cref="DebugEnabled"/> 单独控制
///   （默认关，发布期关掉避免刷屏）；Warn/Error 始终输出（除非全局关）。
/// - 每条自动带 ModId 前缀，统一标识来源。
/// - <see cref="EnabledToFile"/>：纯文本日志文件出口开关。开启后每条日志同步 append 到
///   存档目录 AutoTranslator/autotranslator.log（无 RimWorld/Unity 调用堆栈污染，纯文本一行一条，
///   适合直接打开复制，尤其是批量数据/未翻译 key 清单这类需要复制的内容）。
/// - Warn/Error 每次均输出（不去重）；去重由显式 Once 系（WarnOnce/ErrorOnce）提供，
///   调用方按"该条是否会反复刷屏"自行决定，不去重为默认行为（RimWorld 的 Once 去重本就是可选能力）。
///
/// 分层：基础层（被所有层依赖，自身不依赖上层）。
/// </summary>
public static class AutoLog
{
  /// <summary>全局日志开关。置 false 后所有日志均不输出。默认 true。</summary>
  public static bool Enabled { get; set; } = true;

  /// <summary>调试日志开关。仅影响 Debug 级；发布期置 false 关闭详细日志。默认 false。</summary>
  public static bool DebugEnabled { get; set; }

  /// <summary>
  /// 纯文本日志文件出口开关。置 true 后每条日志同步 append 到存档目录
  /// AutoTranslator/autotranslator.log（纯文本、无堆栈、一行一条，便于复制）。
  /// 与 Enabled 独立：即使 Enabled=false，文件出口仍可单独保留（反之亦然），
  /// 但本实现中文件出口同样受 Enabled 短路保护——全局关闭即全不写。
  /// 默认 false（不落盘，仅走 RimWorld 日志窗口）。
  /// </summary>
  public static bool EnabledToFile { get; set; }

  /// <summary>日志前缀，固定为本模组 ID，便于在 RimWorld 控制台筛选。</summary>
  private static string Prefix => $"[{AutoTranslatorConstants.ModId}] ";

  /// <summary>纯文本日志文件完整路径（mod 包目录/Logs/autotranslator.log）。首次写入时惰性计算并建目录。</summary>
  private static string LogFilePath
  {
    get
    {
      // 放到我们自己的 mod 包目录下独立 Logs/ 子目录，不污染 RimWorld 存档根或其他日志。
      // GenFilePaths.ModsFolderPath 是所有 mod 的根目录，再拼上本 mod 包名（AutoTranslator）子目录。
      var modRoot = System.IO.Path.Combine(GenFilePaths.ModsFolderPath, AutoTranslatorConstants.ModPackageDir);
      return System.IO.Path.Combine(modRoot, AutoTranslatorConstants.LogDirName, AutoTranslatorConstants.LogFileName);
    }
  }

  /// <summary>
  /// 同步将一行纯文本日志 append 到文件（无堆栈、无前缀噪音之外的任何包装）。
  /// 失败静默吞掉：文件出口不应影响游戏主流程，也不应回写 RimWorld 日志造成递归。
  /// </summary>
  private static void AppendToFile(string line)
  {
    try
    {
      var path = LogFilePath;
      var dir = System.IO.Path.GetDirectoryName(path);
      if (!string.IsNullOrEmpty(dir))
      {
        System.IO.Directory.CreateDirectory(dir);
      }
      System.IO.File.AppendAllText(path, line + System.Environment.NewLine);
    }
    catch (Exception)
    {
      // 文件出口失败不应影响主流程，也不回写日志（避免递归）。
    }
  }

  /// <summary>调试级：开发期排错用，受 DebugEnabled 控制。</summary>
  public static void Debug(string text)
  {
    if (!Enabled || !DebugEnabled)
    {
      return;
    }
    var full = Prefix + text;
    Log.Message(full);
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
    Log.Message(full);
    if (EnabledToFile)
    {
      AppendToFile($"[{Now}] INFO {full}");
    }
  }

  /// <summary>警告级：可恢复的非预期情况（如写盘失败）。每次均输出（不去重）。</summary>
  public static void Warn(string text)
  {
    if (!Enabled)
    {
      return;
    }
    var full = Prefix + text;
    Log.Warning(full);
    if (EnabledToFile)
    {
      AppendToFile($"[{Now}] WARN {full}");
    }
  }

  /// <summary>警告级（单次）：相同文本只输出一次，用字符串 hash 作为稳定 key。用于会反复触发的噪声。</summary>
  public static void WarnOnce(string text)
  {
    if (!Enabled)
    {
      return;
    }
    var full = Prefix + text;
    Log.WarningOnce(full, text.GetHashCode());
    if (EnabledToFile)
    {
      AppendToFile($"[{Now}] WARN {full}");
    }
  }

  /// <summary>错误级：不可恢复或严重影响功能的问题。每次均输出（不去重）。</summary>
  public static void Error(string text)
  {
    if (!Enabled)
    {
      return;
    }
    var full = Prefix + text;
    Log.Error(full);
    if (EnabledToFile)
    {
      AppendToFile($"[{Now}] ERROR {full}");
    }
  }

  /// <summary>错误级（单次）：相同文本只输出一次，用字符串 hash 作为稳定 key。用于会反复触发的噪声。</summary>
  public static void ErrorOnce(string text)
  {
    if (!Enabled)
    {
      return;
    }
    var full = Prefix + text;
    Log.ErrorOnce(full, text.GetHashCode());
    if (EnabledToFile)
    {
      AppendToFile($"[{Now}] ERROR {full}");
    }
  }

  /// <summary>日志时间戳（文件出口用，避免 ToString 时区歧义，用本地时间 ISO 近似格式）。</summary>
  private static string Now => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
}
