using System.IO;
using System.Xml.Linq;
using Verse;

namespace nomadicooer.AutoTranslator;

/// <summary>
/// 将未翻译的 key 写入当前激活语言包的 Keyed/auto.xml。
/// 作为独立的写文件工具，供 AutoTranslatorEngine（后台线程）调用，
/// 避免 TranslationPatch 与 AutoTranslatorEngine 互相引用。
/// </summary>
public static class AutoXmlWriter
{
    // 写入当前激活语言包的 Keyed 目录（而非固定 English）。
    // 仅由后台线程在非默认语言下调用，故 activeLanguage 必为非 null 且非 English。
    private static string AutoXmlDir => Path.Combine(AutoTranslatorStartup.RootDir, "Languages", LanguageDatabase.activeLanguage.folderName, "Keyed");
    private static string AutoXmlPath => Path.Combine(AutoXmlDir, AutoTranslatorConstants.AutoXmlFileName);

    /// <summary>
    /// 将 key 写入当前激活语言包的 auto.xml。
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
