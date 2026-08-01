using System;
using System.Collections.Generic;
using System.Reflection;
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

    /// <summary>
    /// 平台标志（完整 PlatformNameKey，如 "AutoTranslator.Volcengine"） → 对应 TranslatePlatform 子类的缓存。
    /// 启动时反射扫描一次，之后按 cred.PlatformId（即 PlatformNameKey）直接取类型实例化，
    /// 不再写死的 switch，新增/删除平台只改对应引擎类本身。
    /// </summary>
    private static readonly Dictionary<string, Type> PlatformTypes = ScanPlatformTypes();

    /// <summary>
    /// 反射扫描本程序集内所有 TranslatePlatform 子类，读取其公共常量 PlatformNameKey
    /// （"AutoTranslator.Volcengine" 形式）作为平台标志建索引。
    /// </summary>
    private static Dictionary<string, Type> ScanPlatformTypes()
    {
        var map = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (Type type in Assembly.GetExecutingAssembly().GetTypes())
        {
            if (type.IsAbstract || type.IsInterface || !typeof(TranslatePlatform).IsAssignableFrom(type))
            {
                continue;
            }

            // 找公共常量 PlatformNameKey（引擎类已改为 public）
            FieldInfo field = type.GetField("PlatformNameKey", BindingFlags.Public | BindingFlags.Static);
            if (field == null || !field.IsLiteral || field.FieldType != typeof(string))
            {
                continue;
            }

            string fullKey = (string)field.GetValue(null);
            if (!string.IsNullOrEmpty(fullKey))
            {
                map[fullKey] = type;
            }
        }

        return map;
    }

    /// <summary>
    /// 按平台标志（cred.PlatformId，应为完整 PlatformNameKey）实例化翻译平台
    /// （构造函数签名为 (string accessKey, string secretKey)）。
    /// 优先用完整 PlatformNameKey 匹配；找不到时兼容旧存档里的裸短名（去前缀形式）。
    /// 找不到对应类型返回 null。
    /// </summary>
    private static TranslatePlatform CreatePlatform(string platformId, string accessKey, string secretKey)
    {
        string resolved = platformId;
        if (!PlatformTypes.TryGetValue(resolved, out Type type))
        {
            // 兼容旧存档：cred.PlatformId 可能是不带前缀的裸短名（如 "Volcengine"）
            string prefixed = AutoTranslatorConstants.TranslationKeyPrefix + platformId;
            if (PlatformTypes.TryGetValue(prefixed, out type))
            {
                resolved = prefixed;
            }
            else
            {
                Logger.Warning("未知翻译平台: " + platformId);
                return null;
            }
        }

        try
        {
            return (TranslatePlatform)Activator.CreateInstance(type, accessKey, secretKey);
        }
        catch (Exception ex)
        {
            Logger.Warning("创建翻译平台实例失败: " + resolved + " -> " + ex.Message);
            return null;
        }
    }

    static AutoTranslatorStartup()
    {
        var pack = LoadedModManager.GetMod(typeof(AutoTranslatorMod))?.Content;
        RootDir = pack?.RootDir;

        Harmony = new Harmony(AutoTranslatorConstants.ModId);
        Harmony.PatchAll();

        // 构建翻译调度器：仅注册已在设置中填入凭据的平台，避免空凭据打 API 浪费。
        // 总开关关闭时（EnableAutoTranslate=false）不启动引擎：不再调用翻译 API、不再写新 key，
        // 但本模组仍加载，已写入的 auto.xml 译文继续随语言包生效。
        var settings = AutoTranslatorMod.Settings;
        if (settings != null && AutoTranslatorMod.AutoTranslateEnabled)
        {
            var sources = new List<TranslatePlatform>();
            foreach (PlatformCredential cred in settings.Credentials)
            {
                if (string.IsNullOrEmpty(cred.AccessKey) || string.IsNullOrEmpty(cred.SecretKey))
                {
                    continue; // 凭据不完整则跳过
                }

                // 按 cred.PlatformId 匹配引擎类的 PlatformNameKey（去前缀），自动实例化，无写死 switch。
                TranslatePlatform source = CreatePlatform(cred.PlatformId, cred.AccessKey, cred.SecretKey);
                if (source != null)
                {
                    sources.Add(source);
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
