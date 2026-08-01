using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Verse;

namespace nomadicooer.AutoTranslator;

/// <summary>
/// 未翻译 key 的文件读写工具，写到本模组自身语言包目录的 Keyed/ 下，单文件 auto.xml。
/// auto.xml 的 value 含义随模式不同：
///   - 实时翻译开启：value 为译文（翻译成功后写入）。
///   - 实时翻译关闭：value 为默认语言（英文）原文内容（仅记录待翻译 key 与原文）。
/// 引擎据此判断是否需要翻译：value 与默认语言内容相同即为"未翻译"，需调 API。
/// 作为独立底层工具供 AutoTranslatorEngine（后台线程）单向依赖，
/// 避免 TranslationPatch 与 AutoTranslatorEngine 互相引用。
/// 默认语言 English（激活语言即默认语言）不写文件：英文地区无需翻译。
/// </summary>
public static class AutoXmlWriter
{
    // 本模组自身语言包目录：.../AutoTranslator/Languages/<语言folderName>/Keyed/
    // 优先从运行中的本模组（packageId == ModId）取 RootDir（最准确），兜底用启动期记录值。
    private static string ModRootDir
    {
        get
        {
            foreach (ModContentPack mod in LoadedModManager.RunningMods)
            {
                if (mod.PackageId == AutoTranslatorConstants.ModId)
                {
                    return mod.RootDir;
                }
            }

            return AutoTranslatorStartup.RootDir;
        }
    }

    // 当前激活语言包对应的 Keyed 目录。默认语言 English（激活语言==默认语言）直接返回 null 跳过：
    // 英文地区无需翻译，不拦截、不写文件。
    private static string KeyedDir
    {
        get
        {
            if (LanguageDatabase.activeLanguage == null
                || LanguageDatabase.activeLanguage.folderName == LanguageDatabase.DefaultLangFolderName)
            {
                return null;
            }

            return Path.Combine(ModRootDir, "Languages", LanguageDatabase.activeLanguage.folderName, "Keyed");
        }
    }

    private static string AutoXmlPath => Path.Combine(KeyedDir, AutoTranslatorConstants.AutoXmlFileName);

    /// <summary>
    /// 将 key 写入 auto.xml。
    /// overwrite=true 时（翻译成功写译文）覆盖已有记录；
    /// overwrite=false 时（记录英文原文）仅在尚无记录时写入，避免覆盖已成功的译文。
    /// KeyedDir 为空（无激活语言）时直接跳过。
    /// </summary>
    public static void WriteAutoKey(string key, string value, bool overwrite = false)
    {
        try
        {
            if (KeyedDir == null || string.IsNullOrEmpty(key))
            {
                return;
            }

            // 确保目录存在（当前语言包的 Keyed 目录可能缺失）
            Directory.CreateDirectory(KeyedDir);

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

            var existing = root.Element(key);
            if (existing != null)
            {
                if (!overwrite)
                {
                    return; // 已有记录且非覆盖模式，跳过（保留已记录/已翻译内容）
                }

                existing.Value = value;
            }
            else
            {
                root.Add(new XElement(key, value));
            }

            doc.Save(AutoXmlPath);
        }
        catch (System.Exception ex)
        {
            Logger.Warning("写入 auto.xml 失败: " + ex.Message);
        }
    }

    /// <summary>
    /// 读取 auto.xml 中所有 key -> value 的映射。文件不存在返回空字典。
    /// 供"一键翻译"判断哪些 key 的 value 仍等于默认语言内容（即未翻译）。
    /// </summary>
    public static Dictionary<string, string> LoadAutoKeys()
    {
        var result = new Dictionary<string, string>(System.StringComparer.Ordinal);
        try
        {
            if (KeyedDir == null || !File.Exists(AutoXmlPath))
            {
                return result;
            }

            var doc = XDocument.Load(AutoXmlPath);
            if (doc.Root == null)
            {
                return result;
            }

            foreach (XElement el in doc.Root.Elements())
            {
                if (!string.IsNullOrEmpty(el.Name.LocalName))
                {
                    result[el.Name.LocalName] = el.Value ?? string.Empty;
                }
            }
        }
        catch (System.Exception ex)
        {
            Logger.Warning("读取 auto.xml 失败: " + ex.Message);
        }

        return result;
    }
}
