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
            foreach (PlatformCredential cred in settings.Credentials)
            {
                if (string.IsNullOrEmpty(cred.AccessKey) || string.IsNullOrEmpty(cred.SecretKey))
                {
                    continue; // 凭据不完整则跳过
                }

                switch (cred.PlatformId)
                {
                    case "Volcengine":
                        sources.Add(new VolcengineTranslate(cred.AccessKey, cred.SecretKey));
                        break;
                    case "Aliyun":
                        sources.Add(new AliyunTranslate(cred.AccessKey, cred.SecretKey));
                        break;
                    case "Baidu":
                        sources.Add(new BaiduTranslate(cred.AccessKey, cred.SecretKey));
                        break;
                }
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
