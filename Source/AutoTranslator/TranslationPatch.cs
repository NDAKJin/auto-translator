using System.Xml.Linq;
using System.IO;
using HarmonyLib;
using Verse;

namespace nomadicooer.AutoTranslator;

/// <summary>
/// 拦截翻译失败（TryTranslate 返回 false）的调用。
/// 目标：Verse.Translator.TryTranslate(this string key, out TaggedString result)
/// 注意：这是扩展方法，Harmony 把 this 参数视为实例目标。
/// 翻译失败时：仅将 key 写入当前激活语言包的 Keyed/auto.xml 做记录，
/// value 取默认语言（English）的英原文作为补译参考。不修改翻译结果与返回值。
/// 不调用翻译引擎，仅做标记与回退，性能开销极小。
/// </summary>
[HarmonyPatch(typeof(Translator), nameof(Translator.TryTranslate))]
public static class TranslationPatch
{
    // 写入当前激活语言包的 Keyed 目录（而非固定 English）。
    // 仅由 TryTranslatePostfix 在非默认语言下调用，故 activeLanguage 必为非 null 且非 English。
    private static string AutoXmlDir => Path.Combine(AutoTranslatorStartup.RootDir, "Languages", LanguageDatabase.activeLanguage.folderName, "Keyed");
    private static string AutoXmlPath => Path.Combine(AutoXmlDir, AutoTranslatorConstants.AutoXmlFileName);
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
        // 记录原文的未翻译 key 到 auto.xml（持久化，供补译参考）。
        WriteAutoKey(key, saveresult);

        // 提交异步翻译：后台线程翻译完成后实时回写 RimWorld 字典，下次查询即命中。
        // source 固定为默认语言（英文），target 为当前激活语言。
        AutoTranslatorEngine.RequestTranslate(
            key,
            LanguageDatabase.defaultLanguage.folderName,
            LanguageDatabase.activeLanguage.folderName);
    }

    private static void WriteAutoKey(string key, string originalKey)
    {
        try
        {
            // 确保目录存在（当前语言包的 Keyed 目录可能缺失）
            Directory.CreateDirectory(AutoXmlDir);

            XDocument doc;
            if (File.Exists(AutoXmlPath))
            {
                doc = XDocument.Load(AutoXmlPath);
            }
            else
            {
                doc = new XDocument(new XElement("LanguageData"));
            }

            var root = doc.Root;
            if (root == null)
            {
                return;
            }

            // 已存在则跳过
            if (root.Element(key) != null)
            {
                return;
            }

            // value 写默认语言（English）的英原文，便于阅读与补译
            root.Add(new XElement(key, originalKey));
            doc.Save(AutoXmlPath);
        }
        catch (System.Exception ex)
        {
            Logger.Warning("写入 auto.xml 失败: " + ex.Message);
        }
    }
}
