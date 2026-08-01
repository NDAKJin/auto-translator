using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Verse;

namespace nomadicooer.AutoTranslator;

/// <summary>
/// 未翻译 key 的文件读写工具，写到本模组自身语言包目录的 Keyed/ 下，单文件 auto.xml。
/// 每条记录带显式 <c>translated</c> 属性，避免依赖"value 是否等于原文"这种脆弱推断：
///   - translated="false"：待翻译，value 为默认语言（英文）原文内容（记录阶段）。
///   - translated="true" ：已翻译，value 为译文（翻译成功后写入）。
/// "一键翻译"只挑 translated="false" 的条目提交，翻译成功改写成 true，
/// 彻底杜绝"引擎返回原文导致永远判定未翻译、反复调 API"的死循环。
/// 作为独立底层工具供 AutoTranslatorEngine（后台线程）单向依赖，
/// 避免 TranslationPatch 与 AutoTranslatorEngine 互相引用。
/// 默认语言 English（激活语言即默认语言）不写文件：英文地区无需翻译。
/// </summary>
public static class AutoXmlWriter
{
    /// <summary>
    /// auto.xml 中单条记录的结构化表示。
    /// </summary>
    public sealed class AutoKeyEntry
    {
        /// <summary>翻译 key（同时是 xml 元素名）。</summary>
        public string Key;

        /// <summary>value：待翻译时为默认语言原文，已翻译时为译文。</summary>
        public string Value;

        /// <summary>是否已翻译完成。false=待翻译，true=已翻译。</summary>
        public bool Translated;
    }

    /// <summary>
    /// xml 属性名：标记该条记录是否已翻译。
    /// 注意：RimWorld 的 LanguageData 加载器会忽略未知属性（仅读取元素文本为 key 的 value），
    /// 因此该属性不会污染语言包加载，仅作为本模组的私有元数据。
    /// </summary>
    private const string TranslatedAttr = "translated";

    /// <summary>
    /// 已落盘 key 的内存索引（懒加载，首次写入时从 auto.xml 读入）。
    /// 存在必要性：TranslationPatch 在实时翻译关闭时每帧都会对同一个未翻译 key 调用
    /// <see cref="WriteAutoKey"/>，若不去重，每帧都要 File.Exists + XDocument.Load 全文件解析，
    /// 界面上几十个未翻译 key × 60FPS 会直接拖垮游戏。
    /// 有了索引后，写过的 key 只需一次 HashSet 查询即返回，零 IO。
    /// value 为 translated 标记：已翻译的 key 在非覆盖模式下同样跳过。
    /// </summary>
    private static Dictionary<string, bool> _writtenKeys;

    /// <summary>索引对应的语言 folderName，切语言后据此判定索引失效并重建。</summary>
    private static string _indexedFolderName;

    /// <summary>保护 <see cref="_writtenKeys"/>：后台翻译任务与主线程 postfix 可能并发写入。</summary>
    private static readonly object IndexLock = new object();

    /// <summary>
    /// 确保内存索引已就绪且与当前语言匹配（假定 <see cref="IndexLock"/> 已持有）。
    /// 语言切换后 auto.xml 换了目录，旧索引必须丢弃重建。
    /// </summary>
    private static void EnsureIndex(string folderName)
    {
        if (_writtenKeys != null && _indexedFolderName == folderName)
        {
            return;
        }

        _writtenKeys = new Dictionary<string, bool>(System.StringComparer.Ordinal);
        _indexedFolderName = folderName;
        foreach (KeyValuePair<string, AutoKeyEntry> kv in LoadAutoKeys())
        {
            _writtenKeys[kv.Key] = kv.Value.Translated;
        }
    }

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
    /// 将 key 写入 auto.xml（新 key 立即持久化，不做批量延迟）。
    /// translated 标记该条记录是否已翻译：记录原文阶段传 false，写译文阶段传 true。
    /// overwrite=true 时（翻译成功写译文）覆盖已有记录（含 translated 属性）；
    /// overwrite=false 时（记录英文原文）仅在尚无记录时写入，避免覆盖已成功的译文。
    /// KeyedDir 为空（无激活语言/英文地区）时直接跳过。
    ///
    /// 写过的 key 由 <see cref="_writtenKeys"/> 内存索引拦截，直接返回不碰磁盘——
    /// 这是必要优化：postfix 会对同一未翻译 key 每帧调用一次，否则将每帧全文件解析 xml。
    /// </summary>
    public static void WriteAutoKey(string key, string value, bool translated, bool overwrite = false)
    {
        try
        {
            if (KeyedDir == null || string.IsNullOrEmpty(key))
            {
                return;
            }

            // 内存索引短路：写过且非覆盖模式，直接返回，零磁盘 IO。
            lock (IndexLock)
            {
                EnsureIndex(LanguageDatabase.activeLanguage.folderName);
                if (_writtenKeys.ContainsKey(key) && !overwrite)
                {
                    return;
                }
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
                existing.SetAttributeValue(TranslatedAttr, translated ? "true" : "false");
            }
            else
            {
                var el = new XElement(key, value);
                el.SetAttributeValue(TranslatedAttr, translated ? "true" : "false");
                root.Add(el);
            }

            doc.Save(AutoXmlPath);

            // 落盘成功后再更新索引：保存失败时索引不被污染，下次仍会重试写入。
            lock (IndexLock)
            {
                if (_writtenKeys != null)
                {
                    _writtenKeys[key] = translated;
                }
            }
        }
        catch (System.Exception ex)
        {
            Logger.Warning("写入 auto.xml 失败: " + ex.Message);
        }
    }

    /// <summary>
    /// 读取 auto.xml 中所有 key -> <see cref="AutoKeyEntry"/> 的映射。文件不存在返回空字典。
    /// 供"一键翻译"筛选 translated=false 的待翻译条目（不再依赖内容比对）。
    /// </summary>
    public static Dictionary<string, AutoKeyEntry> LoadAutoKeys()
    {
        var result = new Dictionary<string, AutoKeyEntry>(System.StringComparer.Ordinal);
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
                if (string.IsNullOrEmpty(el.Name.LocalName))
                {
                    continue;
                }

                string attr = el.Attribute(TranslatedAttr)?.Value;
                bool translated = string.Equals(attr, "true", System.StringComparison.OrdinalIgnoreCase);
                result[el.Name.LocalName] = new AutoKeyEntry
                {
                    Key = el.Name.LocalName,
                    Value = el.Value ?? string.Empty,
                    Translated = translated
                };
            }
        }
        catch (System.Exception ex)
        {
            Logger.Warning("读取 auto.xml 失败: " + ex.Message);
        }

        return result;
    }
}
