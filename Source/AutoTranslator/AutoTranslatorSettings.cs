using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace nomadicooer.AutoTranslator;


public class AutoTranslatorSettings : ModSettings
{
    // 基本设置：三个开关（持久化到 ModConfigs.xml，不进 git）
    // 开启动态翻译：拦截无 key 文本做动态翻译，有 key 的仍由系统管理
    public bool enableAutoTranslate = true;
    // 启动翻译记录：记录翻译过程/结果
    public bool enableTranslationLog;
    // 启用实时翻译：实时翻译
    public bool enableRealtimeUpdate;

    public override void ExposeData()
    {
        Scribe_Values.Look(ref enableAutoTranslate, "enableAutoTranslate", true);
        Scribe_Values.Look(ref enableTranslationLog, "enableTranslationLog", false);
        Scribe_Values.Look(ref enableRealtimeUpdate, "enableRealtimeUpdate", false);
    }

    // 缓存语言信息行，按激活语言对象引用失效（语言切换会重建数据，引用必变）
    private static string cachedActiveLangLine;
    private static string cachedDefaultLangLine;
    private static string cachedBasicInfoTitle;
    private static LoadedLanguage cachedActiveLang;

    // 缓存激活/默认语言两行文本及标题，按激活语言对象引用失效，避免每帧重复 Translate + 拼接
    private static void EnsureLangLines()
    {
        if (cachedActiveLangLine != null
            && cachedDefaultLangLine != null
            && cachedBasicInfoTitle != null
            && ReferenceEquals(cachedActiveLang, LanguageDatabase.activeLanguage))
        {
            return;
        }

        cachedActiveLangLine = string.Format(
            TranslateKeys.ActiveLanguage.Translate(),
            LanguageDatabase.activeLanguage.folderName);
        cachedDefaultLangLine = string.Format(
            TranslateKeys.DefaultLanguage.Translate(),
            LanguageDatabase.defaultLanguage.folderName);
        cachedBasicInfoTitle = TranslateKeys.BasicInfo.Translate();
        cachedActiveLang = LanguageDatabase.activeLanguage;
    }

    // 此处添加rimworld绘制内容  
    public void DoSettingsWindowContents(Rect inRect)
    {
        Listing_Standard listingStandard = new();
        listingStandard.Begin(inRect);
        EnsureLangLines();
        Title.Draw(listingStandard, cachedBasicInfoTitle);
        listingStandard.Label($"{cachedActiveLangLine}    {cachedDefaultLangLine}");
        listingStandard.Gap(6f);
        listingStandard.CheckboxLabeled(TranslateKeys.EnableAutoTranslate.Translate(), ref enableAutoTranslate);
        listingStandard.CheckboxLabeled(TranslateKeys.EnableTranslationLog.Translate(), ref enableTranslationLog);
        listingStandard.CheckboxLabeled(TranslateKeys.EnableRealtimeUpdate.Translate(), ref enableRealtimeUpdate);
        listingStandard.End();
    }
}
