using System.IO;
using System.Xml.Linq;
using Verse;

namespace nomadicooer.AutoTranslator;

/// <summary>
/// 将未翻译的 key 写入本模组自身语言包目录的 Keyed/auto.xml。
/// 拦截到的 key 不携带来源 Mod 信息，无法归到具体 Mod，因此统一写本模组目录，
/// 由 RimWorld 的语言加载机制随本模组一起生效。
/// 作为独立的写文件工具，供 AutoTranslatorEngine（后台线程）调用，
/// 避免 TranslationPatch 与 AutoTranslatorEngine 互相引用。
/// </summary>
public static class AutoXmlWriter
{
    // 本模组自身语言包目录：.../AutoTranslator/Languages/<语言folderName>/Keyed/
    // 由 RunningMods 中本模组（packageId == ModId）的 RootDir 取，再叠加语言目录。
    private static string ModRootDir
    {
        get
        {
            // 优先从运行中的本模组取 RootDir（最准确）。
            foreach (ModContentPack mod in LoadedModManager.RunningMods)
            {
                if (mod.PackageId == AutoTranslatorConstants.ModId)
                {
                    return mod.RootDir;
                }
            }

            // 兜底：用启动期记录的本模组 RootDir。
            return AutoTranslatorStartup.RootDir;
        }
    }

    // English 作为源语言基准包，不写 auto.xml，返回 null 跳过。
    private static string AutoXmlDir
    {
        get
        {
            if (LanguageDatabase.activeLanguage.folderName == LanguageDatabase.DefaultLangFolderName)
            {
                return null;
            }

            return Path.Combine(ModRootDir, "Languages", LanguageDatabase.activeLanguage.folderName, "Keyed");
        }
    }

    private static string AutoXmlPath => Path.Combine(AutoXmlDir, AutoTranslatorConstants.AutoXmlFileName);

    /// <summary>
    /// 将 key 写入本模组语言包目录的 auto.xml。
    /// overwrite=true 时（翻译成功写译文）覆盖已有记录；
    /// overwrite=false 时（翻译失败写原文）仅在尚无记录时写入，避免覆盖已成功的译文。
    /// 当前语言为 English（源语言基准包）时直接跳过，不写文件。
    /// </summary>
    public static void WriteAutoKey(string key, string value, bool overwrite = false)
    {
        try
        {
            // English 为源语言基准包，不写 auto.xml
            if (AutoXmlDir == null)
            {
                return;
            }

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

            var existing = root.Element(key);
            if (existing != null)
            {
                if (!overwrite)
                {
                    return; // 已有记录且非覆盖模式，跳过（保留更优的译文）
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
}
