using RimWorld;
using Verse;

namespace nomadicooer.AutoTranslator;

/// <summary>
/// 模组主入口。RimWorld 通过 Mod 程序集扫描此类加载设置页。
/// 此类在启动流程中加载得非常早（早于 Def 就绪），仅用于设置页与生命周期入口，
/// 不要在此处做依赖 Def、纹理或图形资源的初始化。
/// </summary>
public class AutoTranslatorMod : Mod
{
    /// <summary>
    /// 模组实例（供静态上下文访问设置）。
    /// </summary>
    public static AutoTranslatorMod Instance { get; private set; }

    private readonly AutoTranslatorSettings settings;

    /// <summary>
    /// 是否启用翻译键记录（供 TranslationPatch 在静态上下文读取）。
    /// </summary>
    public static bool KeyRecordingEnabled => Instance?.settings.EnableKeyRecording ?? true;

    /// <summary>
    /// 是否实时翻译（供 TranslationPatch 在静态上下文读取）。
    /// </summary>
    public static bool RealtimeUpdateEnabled => Instance?.settings.EnableRealtimeUpdate ?? true;

    public AutoTranslatorMod(ModContentPack content) : base(content)
    {
        Instance = this;
        settings = GetSettings<AutoTranslatorSettings>();
        Logger.Message("已加载。");
    }

    public override string SettingsCategory()
    {
        return AutoTranslatorSettings.SettingsCategoryKey.Translate();
    }

    public override void DoSettingsWindowContents(UnityEngine.Rect inRect)
    {
        var listing = new Listing_Standard();
        listing.Begin(inRect);

        // 当前激活语言（只读显示）
        listing.Label(
            AutoTranslatorSettings.ActiveLanguageKey.Translate(
                LanguageDatabase.activeLanguage?.DisplayName ?? "—"));

        // 默认语言（只读显示）
        listing.Label(
            AutoTranslatorSettings.DefaultLanguageKey.Translate(
                LanguageDatabase.defaultLanguage?.DisplayName ?? "—"));

        // 是否启用翻译键记录
        listing.CheckboxLabeled(
            AutoTranslatorSettings.EnableKeyRecordingKey.Translate(),
            ref settings.EnableKeyRecording);

        // 是否实时更新未翻译字串
        listing.CheckboxLabeled(
            AutoTranslatorSettings.EnableRealtimeUpdateKey.Translate(),
            ref settings.EnableRealtimeUpdate);

        listing.End();
    }
}
