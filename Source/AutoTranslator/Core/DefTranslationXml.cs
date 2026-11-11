using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

namespace nomadicooer.AutoTranslator;

internal sealed class DefTranslationRecord
{
    public string DefType;
    public string DefName;
    public string Field;
    public string Source;
    public string Translation;

    public bool Translated => !string.IsNullOrEmpty(Translation);
}

internal static class DefTranslationXml
{
    public static string Key(string defType, string defName, string field) =>
        defType + "\0" + defName + "\0" + field;

    public static string WriteRecords(IEnumerable<DefTranslationRecord> records)
    {
        return WriteXml(writer =>
        {
            writer.WriteStartElement("DefTranslations");
            foreach (DefTranslationRecord record in records.OrderBy(r => r.DefType).ThenBy(r => r.DefName).ThenBy(r => r.Field))
            {
                writer.WriteStartElement("Entry");
                writer.WriteAttributeString("type", record.DefType);
                writer.WriteAttributeString("def", record.DefName);
                writer.WriteAttributeString("field", record.Field);
                writer.WriteAttributeString("source", record.Source);
                if (record.Translated)
                {
                    writer.WriteString(record.Translation);
                }
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        });
    }

    public static List<DefTranslationRecord> ReadRecords(string xml)
    {
        var result = new List<DefTranslationRecord>();
        var document = new XmlDocument { XmlResolver = null };
        document.LoadXml(xml);
        if (document.DocumentElement?.Name != "DefTranslations")
        {
            return result;
        }
        foreach (XmlNode node in document.DocumentElement.ChildNodes)
        {
            if (node.Name != "Entry") continue;
            string defType = node.Attributes?["type"]?.Value;
            string defName = node.Attributes?["def"]?.Value;
            string field = node.Attributes?["field"]?.Value;
            if (string.IsNullOrWhiteSpace(defType) || string.IsNullOrWhiteSpace(defName)
                || (field != "label" && field != "description")) continue;
            result.Add(new DefTranslationRecord
            {
                DefType = defType,
                DefName = defName,
                Field = field,
                Source = node.Attributes?["source"]?.Value ?? string.Empty,
                Translation = node.InnerText
            });
        }
        return result;
    }

    public static string WriteDefInjected(IEnumerable<DefTranslationRecord> records)
    {
        return WriteXml(writer =>
        {
            writer.WriteStartElement("LanguageData");
            foreach (DefTranslationRecord record in records.Where(r => r.Translated)
                         .OrderBy(r => r.DefName).ThenBy(r => r.Field))
            {
                writer.WriteElementString(record.DefName + "." + record.Field, record.Translation);
            }
            writer.WriteEndElement();
        });
    }

    private static string WriteXml(Action<XmlWriter> write)
    {
        var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true, OmitXmlDeclaration = false };
        using var buffer = new MemoryStream();
        using (XmlWriter writer = XmlWriter.Create(buffer, settings))
        {
            write(writer);
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
