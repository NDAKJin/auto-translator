using System.Collections.Generic;
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

    /// <summary>设置页滚动视图的纵向位移状态（跨帧保留）。</summary>
    private UnityEngine.Vector2 settingsScrollPos;

    public override void DoSettingsWindowContents(UnityEngine.Rect inRect)
    {
        // 设置页内容较多，使用滚动视图以适配较小的设置窗口。
        // 内部 viewRect 高度预留足够空间，确保窗口高度受限时仍可滚动到底。
        const float ScrollBarMargin = 16f;
        UnityEngine.Rect outRect = inRect;
        const float ViewHeight = 720f;
        UnityEngine.Rect viewRect = new UnityEngine.Rect(
            0f,
            0f,
            inRect.width - ScrollBarMargin,
            ViewHeight);

        // 提前记录本帧"用户在平台列表上勾选的状态"——避免在 listing 渲染期间
        // 增删 settings.Credentials 触发 Ref 编辑器状态错乱（也便于诊断异常）。
        // 先用 List<PlatformId, enabled> 收集，listing 全部画完后再统一同步到 Credentials。
        var platformChecks = new Dictionary<string, bool>();
        foreach (string platformId in AutoTranslatorSettings.PlatformIds)
        {
            platformChecks[platformId] = settings.HasPlatform(platformId);
        }

        try
        {
            Widgets.BeginScrollView(outRect, ref settingsScrollPos, viewRect);

            var listing = new Listing_Standard();
            listing.Begin(viewRect);

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
                bool enabled = platformChecks[platformId];
                listing.CheckboxLabeled(
                    AutoTranslatorSettings.PlatformLabelKeys[platformId].Translate(),
                    ref enabled);
                // 收集回写状态，不在此处立即改 Credentials。
                platformChecks[platformId] = enabled;
            }

            // 下方凭据区域：每行凭据使用 TextEntryLabeled，
            // label 自带"平台名 · 字段"标识，避免与全局表头混淆。
            listing.Gap(12f);
            listing.Label(AutoTranslatorSettings.CredentialTableHeaderKey.Translate());

            foreach (PlatformCredential cred in settings.Credentials)
            {
                listing.Gap(8f);

                // 平台名（小标题）
                string platformLabel = AutoTranslatorSettings.PlatformLabelKeys.TryGetValue(
                    cred.PlatformId, out string key) ? key.Translate() : cred.PlatformId;
                listing.Label(platformLabel);

                // 访问键 + 密钥：两行 TextEntryLabeled，label 自带"平台名 · 字段"。
                string accessKey = AutoTranslatorSettings.AccessKeyLabelKey.Translate();
                string secretKey = AutoTranslatorSettings.SecretKeyLabelKey.Translate();
                cred.AccessKey = listing.TextEntryLabeled(
                    platformLabel + " · " + accessKey, cred.AccessKey);
                cred.SecretKey = listing.TextEntryLabeled(
                    platformLabel + " · " + secretKey, cred.SecretKey);
            }

            // 重置默认按钮：清空所有凭据与开关，恢复到首次安装时的初始状态。
            listing.Gap(12f);
            if (listing.ButtonText(AutoTranslatorSettings.ResetDefaultsKey.Translate()))
            {
                settings.ResetToDefaults();
            }

            listing.End();
            Widgets.EndScrollView();
        }
        catch (System.Exception ex)
        {
            // Verse.Window.DoWindowContents 周围已有 try/catch 吞错，
            // 但只抓顶层；listing 内部异常若被吞没，会导致 UI 在某一行静默截断。
            // 主动打到日志便于诊断。
            Log.Error("[AutoTranslator] DoSettingsWindowContents 异常: " + ex);
        }

        // listing 全部画完之后，再把"用户在平台列表上勾选的状态"同步到凭据列表。
        // 这一步在 try/catch 之外、且不在 listing 渲染期间，规避了 repaint 事件里
        // 增删集合导致控件布局错乱的潜在问题。
        foreach (KeyValuePair<string, bool> kv in platformChecks)
        {
            if (kv.Value)
            {
                settings.EnablePlatform(kv.Key);
            }
            else
            {
                settings.DisablePlatform(kv.Key);
            }
        }

        // 设置内容在窗口打开期间被持续编辑（开关、平台勾选、凭据输入），
        // 必须主动写回磁盘，否则仅存在于内存、关闭窗口/退出游戏后丢失。
        // WriteSettings 是 Verse.Mod 的方法（Mod 实例持有 ModSettings 引用），
        // 会序列化 settings.ExposeData 中的字段到 {ModId}.xml。
        WriteSettings();
    }
}
