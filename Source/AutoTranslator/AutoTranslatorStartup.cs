using System.Reflection;
using HarmonyLib;
using Verse;

namespace nomadicooer.AutoTranslator;

/// <summary>
/// 启动初始化：RimWorld 扫描到 [StaticConstructorOnStartup] 后自动执行静态构造。
/// 负责注册 Harmony 补丁并启用翻译引擎（读取设置页已注入的凭据）。
/// 不依赖 Def，仅做静态注册，避免与 Mod 设置页（AutoTranslatorMod）互相耦合。
/// </summary>
[StaticConstructorOnStartup]
public static class AutoTranslatorStartup
{
    static AutoTranslatorStartup()
    {
        Harmony harmony = new Harmony(AutoTranslatorGlobals.ModId);
        harmony.PatchAll(Assembly.GetExecutingAssembly());
        AutoLog.Info("[AutoTranslator] Harmony 补丁已注册");

        // 启用引擎：构建翻译源并同步开关标志（漏译时按需异步翻译，无常驻线程）。填密钥保存后 RefreshSources 即时生效，无需重启。
        AutoTranslatorEngine.RefreshSources();
    }
}
