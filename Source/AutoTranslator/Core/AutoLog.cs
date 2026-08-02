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
/// - Warn/Error 默认即「单次」语义：相同文本（按内容 hash 稳定 key）只输出一次，避免反复刷屏。
///   上游无需手管 key；调用处应避免拼接每次变化的文本（如异常堆栈），否则去重失效。
///
/// 分层：基础层（被所有层依赖，自身不依赖上层）。
/// </summary>
public static class AutoLog
{
  /// <summary>全局日志开关。置 false 后所有日志均不输出。默认 true。</summary>
  public static bool Enabled { get; set; } = true;

  /// <summary>调试日志开关。仅影响 Debug 级；发布期置 false 关闭详细日志。默认 false。</summary>
  public static bool DebugEnabled { get; set; }

  /// <summary>日志前缀，固定为本模组 ID，便于在 RimWorld 控制台筛选。</summary>
  private static string Prefix => $"[{AutoTranslatorConstants.ModId}] ";

  /// <summary>调试级：开发期排错用，受 DebugEnabled 控制。</summary>
  public static void Debug(string text)
  {
    if (!Enabled || !DebugEnabled)
    {
      return;
    }
    Verse.Log.Message(Prefix + text);
  }

  /// <summary>信息级：常规流程提示。</summary>
  public static void Info(string text)
  {
    if (!Enabled)
    {
      return;
    }
    Verse.Log.Message(Prefix + text);
  }

  /// <summary>警告级（单次语义）：可恢复的非预期情况（如写盘失败）。相同文本只输出一次。</summary>
  public static void Warn(string text)
  {
    if (!Enabled)
    {
      return;
    }
    Verse.Log.WarningOnce(Prefix + text, text.GetHashCode());
  }

  /// <summary>错误级（单次语义）：不可恢复或严重影响功能的问题。相同文本只输出一次。</summary>
  public static void Error(string text)
  {
    if (!Enabled)
    {
      return;
    }
    Verse.Log.ErrorOnce(Prefix + text, text.GetHashCode());
  }
}
