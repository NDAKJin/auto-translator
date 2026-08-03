using System;
using System.Diagnostics;

namespace nomadicooer.AutoTranslator;

/// <summary>
/// 翻译 API 层专用日志出口。
/// 与 <see cref="AutoLog"/> 不同：本类刻意不引用 RimWorld（Verse），
/// 以保证翻译引擎可在无游戏环境下独立测试。
/// 仅在 Debug 配置（定义 AUTOTRANSLATOR_LOGGING 符号）下输出，
/// Release 构建中全部编译为空操作，与 AutoLog 的全局开关语义一致。
/// </summary>
public static class ApiLog
{
    private const string Tag = "[AutoTranslator] ";

    /// <summary>调试级：仅 Debug 构建输出，Release 自动跳过。</summary>
    [Conditional("AUTOTRANSLATOR_LOGGING")]
    public static void Debug(string text)
    {
        System.Diagnostics.Debug.WriteLine(Tag + text);
    }
}
