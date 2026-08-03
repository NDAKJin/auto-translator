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
        // 启动阶段预热 store 字典：尽量在补丁生效前把 auto.xml 历史数据载入内存。
        // 若此刻 activeLanguage 尚未就绪（返回 null），加载会安全跳过，运行时首次查询仍会惰性补加载。
        AutoTranslationStore.TryLoadIfNeeded();

        Harmony harmony = new Harmony(AutoTranslatorGlobals.ModId);
        harmony.PatchAll(Assembly.GetExecutingAssembly());
        AutoLog.Info("[AutoTranslator] Harmony 补丁已注册");

        // 启用引擎：构建翻译源并同步开关标志（漏译时按需异步翻译，无常驻线程）。填密钥保存后 RefreshSources 即时生效，无需重启。
        AutoTranslatorEngine.RefreshSources();
    }
}
