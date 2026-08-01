using System.Diagnostics;
using Verse;

namespace nomadicooer.AutoTranslator;

/// <summary>
/// 统一的日志封装层。
/// 通过编译符号 <c>AUTOTRANSLATOR_LOGGING</c> 控制是否输出日志（相当于宏开关）：
/// 定义该符号才会编译出日志调用，发布时去掉即可全局剔除日志开销。
/// 日志前缀统一来自 <see cref="Tag"/> 常量，避免散落的硬编码字符串。
/// </summary>
public static class Logger
{
    private const string Tag = "[AutoTranslator] ";

    [Conditional("AUTOTRANSLATOR_LOGGING")]
    public static void Message(string message) => Log.Message(Tag + message);

    [Conditional("AUTOTRANSLATOR_LOGGING")]
    public static void Warning(string message) => Log.Warning(Tag + message);

    [Conditional("AUTOTRANSLATOR_LOGGING")]
    public static void Error(string message) => Log.Error(Tag + message);
}
