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

    /// <summary>
    /// 模组设置实例（供启动初始化等静态上下文读取凭据等）。
    /// </summary>
    public static AutoTranslatorSettings Settings => Instance?.settings;

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

        // 翻译平台：上方列表（勾选启用哪些平台）
        listing.Label(AutoTranslatorSettings.PlatformListHeaderKey.Translate());
        foreach (string platformId in AutoTranslatorSettings.PlatformIds)
        {
            bool enabled = settings.HasPlatform(platformId);
            listing.CheckboxLabeled(
                AutoTranslatorSettings.PlatformLabelKeys[platformId].Translate(),
                ref enabled);
            // CheckboxLabeled 通过 ref 回写最新勾选状态；同步到凭据列表（幂等）。
            if (enabled)
            {
                settings.EnablePlatform(platformId);
            }
            else
            {
                settings.DisablePlatform(platformId);
            }
        }

        // 下方凭据表格：已启用平台逐行设置访问键与密钥
        listing.Gap(12f);
        listing.Label(AutoTranslatorSettings.CredentialTableHeaderKey.Translate());
        listing.Label(AutoTranslatorSettings.AccessKeyLabelKey.Translate());
        listing.Label(AutoTranslatorSettings.SecretKeyLabelKey.Translate());

        foreach (PlatformCredential cred in settings.Credentials)
        {
            listing.Gap(6f);
            string label = AutoTranslatorSettings.PlatformLabelKeys.TryGetValue(
                cred.PlatformId, out string key) ? key.Translate() : cred.PlatformId;
            listing.Label(label);
            cred.AccessKey = listing.TextEntry(cred.AccessKey);
            cred.SecretKey = listing.TextEntry(cred.SecretKey);
        }

        listing.End();
    }
}
