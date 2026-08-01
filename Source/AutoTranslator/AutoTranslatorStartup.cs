using Verse;
using HarmonyLib;

namespace nomadicooer.AutoTranslator;

/// <summary>
/// 启动初始化类。带 <see cref="StaticConstructorOnStartup"/> 特性，在即将显示主菜单之前、
/// 于主线程中初始化（此时纹理、图形、Def 等资源均已就绪）。
/// 用于注册 Harmony 补丁、预加载翻译词库、缓存资源等需要主线程安全的初始化工作。
/// </summary>
[StaticConstructorOnStartup]
public static class AutoTranslatorStartup
{
    public static string RootDir { get; private set; }

    public static Harmony Harmony { get; private set; }

    static AutoTranslatorStartup()
    {
        var pack = LoadedModManager.GetMod(typeof(AutoTranslatorMod))?.Content;
        RootDir = pack?.RootDir;

        Harmony = new Harmony(AutoTranslatorConstants.ModId);
        Harmony.PatchAll();
    }
}
