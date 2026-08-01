using System.IO;
using System.Xml.Linq;
using Verse;

namespace nomadicooer.AutoTranslator;

/// <summary>
/// 将未翻译的 key 写入 RimWorld 全局语言包覆盖目录的 Keyed/auto.xml。
/// 拦截到的 key 不携带来源 Mod 信息，无法归到具体 Mod，因此统一写全局层，
/// 对所有 Mod 生效，且不依附 AutoTranslator 自身目录（卸载本模组后翻译仍在）。
/// 作为独立的写文件工具，供 AutoTranslatorEngine（后台线程）调用，
/// 避免 TranslationPatch 与 AutoTranslatorEngine 互相引用。
/// </summary>
public static class AutoXmlWriter
{
    // 全局语言包覆盖目录：RimWorld/Data/<语言folderName>/Keyed/
    // 由 Core 模组的 RootDir（.../Data/Core）取父目录得到 .../Data，再叠加语言目录。
    // 该目录默认不存在，首次写入时由 WriteAutoKey 内的 CreateDirectory 创建。
    private static string DataDir
    {
        get
        {
            // 由 Core 模组的 RootDir（.../Data/Core）取父目录得到 .../Data。
            // Core 必然存在；兜底用本模组 RootDir 再上两层到 Data。
            foreach (ModContentPack mod in LoadedModManager.RunningMods)
            {
                if (mod.PackageId == "Core" || mod.PackageId == "Ludeon.RimWorld")
                {
                    return Path.GetDirectoryName(mod.RootDir);
                }
            }

            // 兜底：本模组 RootDir 为 .../AutoTranslator，上两层到 Data
            return Path.GetDirectoryName(Path.GetDirectoryName(AutoTranslatorStartup.RootDir));
        }
    }

    private static string AutoXmlDir => Path.Combine(DataDir, LanguageDatabase.activeLanguage.folderName, "Keyed");
    private static string AutoXmlPath => Path.Combine(AutoXmlDir, AutoTranslatorConstants.AutoXmlFileName);

    /// <summary>
    /// 将 key 写入全局语言包覆盖目录的 auto.xml。
    /// overwrite=true 时（翻译成功写译文）覆盖已有记录；
    /// overwrite=false 时（翻译失败写原文）仅在尚无记录时写入，避免覆盖已成功的译文。
    /// </summary>
    public static void WriteAutoKey(string key, string value, bool overwrite = false)
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
