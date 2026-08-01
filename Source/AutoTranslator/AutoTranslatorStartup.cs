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

        // 引擎启动条件：总开关开启 + 实时翻译开启。
        // 实时翻译开启时 TranslationPatch 会调用 Engine.RequestTranslate，引擎必须在线（后台消费线程在跑），
        // 否则未翻译 key 既不会被实时翻译、也不会被记录到 auto.xml。
        // 引擎启动不依赖"是否配置了平台凭据"：未配凭据时 dispatcher 带空源集合构造，
        // 翻译请求会因 _sources.Count==0 安全返回失败原文，不会崩溃。
        // 玩家运行期在设置页补填凭据后，由 AutoTranslatorMod.WriteSettings 调用
        // AutoTranslatorEngine.RefreshSources() 即时把凭据灌入引擎，无需重启游戏。
        // 总开关关闭（EnableAutoTranslate=false）时不启动引擎：不再调用翻译 API、不再写新 key，
        // 但本模组仍加载，已写入的 auto.xml 译文继续随语言包生效。
        // 实时翻译关闭（RealtimeUpdateEnabled=false）时，未翻译 key 由 TranslationPatch 直接写 auto.xml，
        // 不经过引擎，故无需启动后台线程。
        // 具体"从 Settings.Credentials 构建翻译源、启动后台线程"等逻辑内聚在 AutoTranslatorEngine 中，
        // 这里只按条件触发启动，避免金字塔分层中的反向依赖（下层 Engine 不应回头引用上层 Startup）。
        if (AutoTranslatorMod.AutoTranslateEnabled && AutoTranslatorMod.RealtimeUpdateEnabled)
        {
            AutoTranslatorEngine.Configure();
        }
    }
}
