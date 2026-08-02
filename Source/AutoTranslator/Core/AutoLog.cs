using System;
using System.IO;
using Verse;

namespace nomadicooer.AutoTranslator;

/// <summary>统一日志层：集中所有日志出口，分级 + 全局/调试开关 + 可选纯文本文件出口。</summary>
public static class AutoLog
{
  /// <summary>全局开关：false 时所有日志均不输出。默认 true。</summary>
  public static bool Enabled { get; set; } = true;

  /// <summary>调试开关：仅控制 Debug 级。默认 false。</summary>
  public static bool DebugEnabled { get; set; }

  /// <summary>文件出口开关：true 时每条日志同步 append 到 mod 包目录 Logs/。默认 false。</summary>
  public static bool EnabledToFile { get; set; }

  private static string Prefix => $"[{AutoTranslatorConstants.ModId}] ";

  /// <summary>纯文本日志文件完整路径（mod 包目录/Logs/autotranslator.log）。</summary>
  private static string LogFilePath
  {
    get
    {
      var modRoot = Path.Combine(GenFilePaths.ModsFolderPath, AutoTranslatorConstants.ModPackageDir);
      return Path.Combine(modRoot, AutoTranslatorConstants.LogDirName, AutoTranslatorConstants.LogFileName);
    }
  }

  /// <summary>追加一行到日志文件，失败静默忽略（不影响主流程、不回写日志）。</summary>
  private static void AppendToFile(string line)
  {
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
  }

  /// <summary>调试级：开发期排错，受 DebugEnabled 控制。</summary>
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

  /// <summary>警告级：可恢复的非预期情况，每次均输出。</summary>
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

  /// <summary>警告级（单次）：相同文本只输出一次。</summary>
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

  /// <summary>错误级：严重影响功能的问题，每次均输出。</summary>
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

  /// <summary>错误级（单次）：相同文本只输出一次。</summary>
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

  private static string Now => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
}
