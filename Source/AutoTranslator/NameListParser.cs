using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AutoTranslator
{
    /// <summary>
    /// 纯逻辑词库解析器：把 Names\*.txt 按段（::Section）解析为字典。
    /// 不依赖 RimWorld / Unity，可在 net9.0 单测中直接运行。
    /// 格式约定：
    ///   # 开头为注释行，忽略
    ///   空行忽略
    ///   ::Name 作为段标识，其下每行一个词条归入该段
    /// </summary>
    public static class NameListParser
    {
        public static Dictionary<string, List<string>> Parse(string text)
        {
            var result = new Dictionary<string, List<string>>();
            if (string.IsNullOrEmpty(text))
            {
                return result;
            }

            string currentSection = "";
            if (!result.ContainsKey(currentSection))
            {
                result[currentSection] = new List<string>();
            }

            using (var reader = new StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith("#"))
                    {
                        continue;
                    }

                    if (trimmed.StartsWith("::"))
                    {
                        currentSection = trimmed.Substring(2).Trim();
                        if (!result.ContainsKey(currentSection))
                        {
                            result[currentSection] = new List<string>();
                        }
                    }
                    else
                    {
                        result[currentSection].Add(trimmed);
                    }
                }
            }

            return result;
        }

        /// <summary>取某段的词条；段不存在返回空列表。</summary>
        public static IReadOnlyList<string> GetSection(Dictionary<string, List<string>> data, string section)
        {
            return data.TryGetValue(section, out var list) ? list : new List<string>();
        }
    }
}
