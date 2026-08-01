using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Verse;

namespace nomadicooer.AutoTranslator;

/// <summary>
/// 未翻译 key 的文件读写工具，写到本模组自身语言包目录的 Keyed/ 下。
/// 两类文件：
///   1) auto.xml —— 翻译成果（实时翻译成功写译文，失败写原文回退）；
///   2) pending.xml —— 待翻译清单（实时翻译关闭时只记录 key，不写译文，
///      待玩家点击"一键翻译"后由引擎批量消费并写 auto.xml）。
/// 作为独立底层工具供 AutoTranslatorEngine（后台线程）单向依赖，
/// 避免 TranslationPatch 与 AutoTranslatorEngine 互相引用。
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

    // English 作为源语言基准包，不写 pending.xml，返回 null 跳过。
    private static string PendingXmlDir
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

    private static string PendingXmlPath => Path.Combine(PendingXmlDir, AutoTranslatorConstants.PendingXmlFileName);

    /// <summary>
    /// 将 key 写入 auto.xml。
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

    /// <summary>
    /// 将 key 追加进待翻译清单 pending.xml（去重：已存在则跳过）。
    /// 当前语言为 English（源语言基准包）时直接跳过。
    /// 仅占位记录 key，value 留空（清单不存译文）。
    /// </summary>
    public static void AddPendingKey(string key)
    {
        try
        {
            if (PendingXmlDir == null || string.IsNullOrEmpty(key))
            {
                return;
            }

            Directory.CreateDirectory(PendingXmlDir);

            XDocument doc;
            if (File.Exists(PendingXmlPath))
            {
                doc = XDocument.Load(PendingXmlPath);
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

            // 去重：已记录的 key 不再追加。
            if (root.Elements(key).Any())
            {
                return;
            }

            root.Add(new XElement(key, string.Empty));
            doc.Save(PendingXmlPath);
        }
        catch (System.Exception ex)
        {
            Logger.Warning("写入 pending.xml 失败: " + ex.Message);
        }
    }

    /// <summary>
    /// 读取待翻译清单中所有 key（去重后的集合）。文件不存在返回空集合。
    /// </summary>
    public static HashSet<string> LoadPendingKeys()
    {
        var result = new HashSet<string>(System.StringComparer.Ordinal);
        try
        {
            if (PendingXmlDir == null || !File.Exists(PendingXmlPath))
            {
                return result;
            }

            var doc = XDocument.Load(PendingXmlPath);
            if (doc.Root == null)
            {
                return result;
            }

            foreach (XElement el in doc.Root.Elements())
            {
                if (!string.IsNullOrEmpty(el.Name.LocalName))
                {
                    result.Add(el.Name.LocalName);
                }
            }
        }
        catch (System.Exception ex)
        {
            Logger.Warning("读取 pending.xml 失败: " + ex.Message);
        }

        return result;
    }

    /// <summary>
    /// 从待翻译清单中移除已完成的 key（批量翻译成功后清理）。
    /// 传入的 keys 不存在于清单时忽略。
    /// </summary>
    public static void RemovePendingKeys(IEnumerable<string> keys)
    {
        try
        {
            if (PendingXmlDir == null || !File.Exists(PendingXmlPath))
            {
                return;
            }

            var doc = XDocument.Load(PendingXmlPath);
            var root = doc.Root;
            if (root == null)
            {
                return;
            }

            var toRemove = keys
                .Where(k => !string.IsNullOrEmpty(k))
                .ToHashSet(System.StringComparer.Ordinal);
            if (toRemove.Count == 0)
            {
                return;
            }

            root.Elements()
                .Where(el => toRemove.Contains(el.Name.LocalName))
                .Remove();
            doc.Save(PendingXmlPath);
        }
        catch (System.Exception ex)
        {
            Logger.Warning("清理 pending.xml 失败: " + ex.Message);
        }
    }
}
