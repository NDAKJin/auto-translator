using HarmonyLib;
using Verse;

namespace nomadicooer.AutoTranslator;

/// <summary>
/// 拦截翻译失败（TryTranslate 返回 false）的调用。
/// 目标：Verse.Translator.TryTranslate(this string key, out TaggedString result)
/// 注意：这是扩展方法，Harmony 把 this 参数视为实例目标。
/// 翻译失败时：取默认语言（English）的英原文作为回退参考，提交给 AutoTranslatorEngine
/// 异步翻译；写入 auto.xml 的职责由 AutoXmlWriter 在后台线程完成（本类不直接写文件）。
/// 不修改翻译结果与返回值，性能开销极小。
/// </summary>
[HarmonyPatch(typeof(Translator), nameof(Translator.TryTranslate))]
public static class TranslationPatch
{
    /// <summary>
    /// 后处理：原方法翻译失败时（__result == false），以原始 key 作为回退译文。
    /// </summary>
    [HarmonyPostfix]
    public static void TryTranslatePostfix(string key, ref TaggedString result, ref bool __result)
    {
        if (__result || key == null
            || !AutoTranslatorMod.KeyRecordingEnabled
            || !AutoTranslatorMod.RealtimeUpdateEnabled
            || LanguageDatabase.activeLanguage == null
            || LanguageDatabase.activeLanguage == LanguageDatabase.defaultLanguage)
        {
            return;
        }
        TaggedString saveresult;
        LanguageDatabase.defaultLanguage.TryGetTextFromKey(key, out saveresult);
        string fallback = saveresult.NullOrEmpty() ? key : saveresult.ToString();

        // 提交异步翻译：后台线程翻译完成后实时回写 RimWorld 字典，并写 auto.xml。
        // 成功写译文、失败写原文（fallback）。主线程不写 auto.xml，避免翻译前就落原文。
        // source 固定为默认语言（英文），target 为当前激活语言。
        AutoTranslatorEngine.RequestTranslate(
            key,
            LanguageDatabase.defaultLanguage.folderName,
            LanguageDatabase.activeLanguage.folderName,
            fallback);
    }
}
