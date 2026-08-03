using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace nomadicooer.AutoTranslator;

/// <summary>
/// 模组主入口。RimWorld 通过 Mod 程序集扫描此类加载设置页。
/// 不挂 [StaticConstructorOnStartup]（注册 Harmony 由 AutoTranslatorStartup 负责）。
/// 仅负责设置页渲染与生命周期入口，不在此做依赖 Def 的初始化。
/// </summary>
public class AutoTranslatorMod : Mod
{
    /// <summary>模组实例（供静态上下文访问设置）。</summary>
    public static AutoTranslatorMod Instance { get; private set; }

    /// <summary>是否启用自动翻译（总开关）。开启即拦截并必然记录；关闭后引擎停止、不再调 API。</summary>
    public static bool AutoTranslateEnabled => Instance?.settings.enableAutoTranslate ?? true;

    /// <summary>是否启用实时翻译。开启即边拦截边异步调 API 翻译并回写 keyedReplacements；关闭仅记录 key。</summary>
    public static bool RealtimeUpdateEnabled => Instance?.settings.enableRealtimeUpdate ?? false;

    /// <summary>模组设置实例（供引擎/启动初始化读取凭据）。</summary>
    public static AutoTranslatorSettings Settings => Instance?.settings;

    private readonly AutoTranslatorSettings settings;

    public override string SettingsCategory() => TranslateKeys.SettingsCategory.Translate();

    public AutoTranslatorMod(ModContentPack content) : base(content)
    {
        Instance = this;
        settings = GetSettings<AutoTranslatorSettings>();
    }

    /// <summary>设置页滚动视图的纵向位移状态（跨帧保留）。</summary>
    private Vector2 settingsScrollPos;

    public override void DoSettingsWindowContents(Rect inRect)
    {
        const float ScrollBarMargin = 16f;
        const float ViewHeight = 720f;
        Rect outRect = inRect;
        Rect viewRect = new Rect(0f, 0f, inRect.width - ScrollBarMargin, ViewHeight);

        // 确保所有已注册平台都有凭据对象（升级旧存档/新增平台时补齐），再读取勾选状态。
        settings.EnsureAllPlatforms();

        // 先收集用户在平台列表上的勾选状态，避免 listing 渲染期间增删 Credentials 触发控件错乱。
        var platformChecks = new Dictionary<string, bool>();
        foreach (string platformId in PlatformRegistry.AllPlatformNameKeys)
        {
            platformChecks[platformId] = settings.HasPlatform(platformId);
        }

        try
        {
            Widgets.BeginScrollView(outRect, ref settingsScrollPos, viewRect);
            var listing = new Listing_Standard();
            listing.Begin(viewRect);

            // 基本信息（当前/默认语言）
            Title.Draw(listing, TranslateKeys.BasicInfo.Translate());

            // 三个总开关
            listing.CheckboxLabeled(TranslateKeys.EnableAutoTranslate.Translate(), ref settings.enableAutoTranslate);
            bool prevLog = settings.enableTranslationLog;
            listing.CheckboxLabeled(TranslateKeys.EnableTranslationLog.Translate(), ref settings.enableTranslationLog);
            if (prevLog != settings.enableTranslationLog)
            {
                AutoTranslationStore.SetTranslationLogEnabled(settings.enableTranslationLog);
                AutoLog.Enabled = settings.enableTranslationLog;
            }
            listing.CheckboxLabeled(TranslateKeys.EnableRealtimeUpdate.Translate(), ref settings.enableRealtimeUpdate);

            // 一键翻译：把记录的未翻译 key 批量提交翻译并回写。仅在实时翻译关闭时意义明显。
            if (listing.ButtonText(TranslateKeys.TranslateAll.Translate()))
            {
                AutoTranslatorEngine.TranslateAllPending();
            }

            // 平台勾选区
            listing.Gap(12f);
            Title.Draw(listing, TranslateKeys.PlatformList.Translate());

            foreach (string platformId in PlatformRegistry.AllPlatformNameKeys)
            {
                bool enabled = platformChecks[platformId];
                listing.CheckboxLabeled(PlatformRegistry.LabelKeys[platformId].Translate(), ref enabled);
                platformChecks[platformId] = enabled;
            }

            // 凭据折叠区：每行两行 TextEntryLabeled（访问键 / 密钥）
            listing.Gap(12f);
            DrawCollapsibleHeader(listing, TranslateKeys.CredentialTable.Translate());
            if (!settings.credentialsCollapsed)
            {
                foreach (PlatformCredential cred in settings.credentials.Where(c => c.enabled))
                {
                    listing.Gap(8f);
                    string platformLabel = PlatformRegistry.LabelKeys.TryGetValue(cred.PlatformId, out string key)
                        ? key.Translate()
                        : cred.PlatformId;
                    listing.Label(platformLabel);

                    // 访问键：真实值存 cred.AccessKey（label 字段，隐藏），输入框打码/明文可切换，改后同步写回。
                    cred.AccessKey = DrawPasswordField(
                        listing, platformLabel + " · " + TranslateKeys.AccessKey.Translate(), cred.AccessKey, ref cred.showAccessKey);

                    // 密钥：同上
                    cred.SecretKey = DrawPasswordField(
                        listing, platformLabel + " · " + TranslateKeys.SecretKey.Translate(), cred.SecretKey, ref cred.showSecretKey);
                }
            }

            // 保存设置 / 恢复默认：两个按钮居中并排。
            // 保存设置：明确写盘并刷新引擎（明确保存后才持久化）。
            // 恢复默认：立即重置内存，但需点保存才落盘。
            listing.Gap(12f);
            const float BottomButtonHeight = 28f;
            const float ButtonSpacing = 4f;
            const float CenterButtonWidth = 140f;
            Rect bottomRow = listing.GetRect(BottomButtonHeight);
            float btnW = Mathf.Min(CenterButtonWidth, (bottomRow.width - ButtonSpacing) / 2f);
            float centerGroupW = btnW * 2f + ButtonSpacing;
            float centerX = bottomRow.x + (bottomRow.width - centerGroupW) / 2f;
            Rect saveRect = new Rect(centerX, bottomRow.y, btnW, bottomRow.height).Rounded();
            Rect resetRect = new Rect(centerX + btnW + ButtonSpacing, bottomRow.y, btnW, bottomRow.height).Rounded();
            if (Widgets.ButtonText(saveRect, TranslateKeys.SaveSettings.Translate()))
            {
                WriteSettings();
                // 写盘后刷新引擎翻译源：补填/修改凭据立即生效，无需重启游戏。
                AutoTranslatorEngine.RefreshSources();
                SoundDefOf.Tick_High.PlayOneShot(SoundInfo.OnCamera(MaintenanceType.None));
            }
            if (Widgets.ButtonText(resetRect, TranslateKeys.ResetDefaults.Translate()))
            {
                settings.ResetToDefaults();
                SoundDefOf.Tick_Low.PlayOneShot(SoundInfo.OnCamera(MaintenanceType.None));
            }

            listing.End();
            Widgets.EndScrollView();
        }
        catch (System.Exception ex)
        {
            AutoLog.Error("[AutoTranslator] DoSettingsWindowContents 异常: " + ex);
        }

        // listing 渲染完后再同步平台勾选状态到凭据列表（避开 repaint 期间增删集合）。
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
    }

    /// <summary>绘制加粗放大的可折叠 section 标题，左侧 ▶/▼ 三角形指示折叠状态，可点击切换。</summary>
    private void DrawCollapsibleHeader(Listing_Standard listing, string text)
    {
        const float TriangleWidth = 24f;
        const float HeaderHeight = 30f;
        Rect rect = listing.GetRect(HeaderHeight);
        GameFont previous = Text.Font;
        Text.Font = GameFont.Medium;
        try
        {
            Rect triangleRect = new Rect(rect.x, rect.y + (rect.height - TriangleWidth) / 2f, TriangleWidth, TriangleWidth);
            if (Widgets.ButtonImage(triangleRect, settings.credentialsCollapsed ? TexButton.Reveal : TexButton.Collapse))
            {
                settings.credentialsCollapsed = !settings.credentialsCollapsed;
            }

            Rect textRect = new Rect(rect.x + TriangleWidth + 6f, rect.y, rect.width - TriangleWidth - 6f, rect.height);
            Widgets.Label(textRect, text);
        }
        finally
        {
            Text.Font = previous;
        }
        listing.Gap(4f);
    }

    /// <summary>
    /// 密码框封装：固定宽度 label 右对齐 + 输入框填充到眼睛按钮前 + 每行右侧眼睛按钮切换显示/隐藏。
    /// 真实值存于 realValue（调用方持有的 label 字段，隐藏）；输入框全 * 打码或明文显示，用户改动后返回真实输入同步写回。
    /// </summary>
    private string DrawPasswordField(Listing_Standard listing, string label, string realValue, ref bool showPassword)
    {
        const float LabelWidth = 150f;
        const float EyeButtonWidth = 24f;
        const float FieldSpacing = 6f;

        Rect rect = listing.GetRect(Text.LineHeight);
        GameFont oldFont = Text.Font;
        TextAnchor oldAnchor = Text.Anchor;
        Text.Font = GameFont.Small;
        Text.Anchor = TextAnchor.MiddleRight;

        Rect labelRect = new Rect(rect.x, rect.y, LabelWidth, rect.height).Rounded();
        Rect eyeRect = new Rect(rect.xMax - EyeButtonWidth, rect.y, EyeButtonWidth, rect.height).Rounded();
        Rect fieldRect = new Rect(rect.x + LabelWidth + FieldSpacing, rect.y, rect.width - LabelWidth - EyeButtonWidth - FieldSpacing * 2f, rect.height).Rounded();
        try
        {
            Widgets.Label(labelRect, label);
        }
        finally
        {
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
        }

        if (Widgets.ButtonImage(eyeRect, TexButton.Reveal))
        {
            showPassword = !showPassword;
        }
        TooltipHandler.TipRegion(eyeRect, (showPassword ? TranslateKeys.HidePassword : TranslateKeys.ShowPassword).Translate());

        string safeValue = realValue ?? string.Empty;
        return showPassword
            ? GUI.TextField(fieldRect, safeValue, Text.CurTextFieldStyle)
            : GUI.PasswordField(fieldRect, safeValue, '*', Text.CurTextFieldStyle);
    }
}
