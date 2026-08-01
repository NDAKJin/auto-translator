using System.Collections.Generic;
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

        // 构建翻译调度器：仅注册已在设置中填入凭据的平台，避免空凭据打 API 浪费。
        var settings = AutoTranslatorMod.Settings;
        if (settings != null)
        {
            var sources = new List<TranslatePlatform>();
            if (!string.IsNullOrEmpty(settings.VolcengineAccessKey) && !string.IsNullOrEmpty(settings.VolcengineSecretKey))
            {
                sources.Add(new VolcengineTranslate(settings.VolcengineAccessKey, settings.VolcengineSecretKey));
            }
            if (!string.IsNullOrEmpty(settings.AliyunAccessKeyId) && !string.IsNullOrEmpty(settings.AliyunAccessKeySecret))
            {
                sources.Add(new AliyunTranslate(settings.AliyunAccessKeyId, settings.AliyunAccessKeySecret));
            }
            if (!string.IsNullOrEmpty(settings.BaiduAppId) && !string.IsNullOrEmpty(settings.BaiduSecretKey))
            {
                sources.Add(new BaiduTranslate(settings.BaiduAppId, settings.BaiduSecretKey));
            }

            if (sources.Count > 0)
            {
                AutoTranslatorEngine.Configure(new TranslationDispatcher(sources));
            }
            else
            {
                Logger.Warning("未配置任何翻译平台凭据，自动翻译引擎未启动。");
            }
        }
    }
}
