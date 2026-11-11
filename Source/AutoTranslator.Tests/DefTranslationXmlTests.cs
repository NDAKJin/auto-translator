using System;
using System.Linq;
using System.Xml;
using Xunit;

namespace nomadicooer.AutoTranslator.Tests;

public class DefTranslationXmlTests
{
    [Fact]
    public void RecordsRoundTripAndOnlyTranslatedFieldsAreInjected()
    {
        var records = new[]
        {
            new DefTranslationRecord
            {
                DefType = "ThingDef", DefName = "Windmill", Field = "label",
                Source = "windmill", Translation = "风车"
            },
            new DefTranslationRecord
            {
                DefType = "ThingDef", DefName = "Windmill", Field = "description",
                Source = "Wind & power <{0}>"
            }
        };

        string ledger = DefTranslationXml.WriteRecords(records);
        DefTranslationRecord[] restored = DefTranslationXml.ReadRecords(ledger).ToArray();
        Assert.Equal(2, restored.Length);
        Assert.Equal("Wind & power <{0}>", restored.Single(r => r.Field == "description").Source);
        Assert.Equal("风车", restored.Single(r => r.Field == "label").Translation);

        var document = new XmlDocument { XmlResolver = null };
        document.LoadXml(DefTranslationXml.WriteDefInjected(restored));
        Assert.Equal("风车", document.DocumentElement["Windmill.label"].InnerText);
        Assert.Null(document.DocumentElement["Windmill.description"]);
    }

    [Fact]
    public void InjectionEscapesMarkupAndPreservesPlaceholders()
    {
        var record = new DefTranslationRecord
        {
            DefType = "ThingDef", DefName = "Windmill", Field = "description",
            Source = "Requires {0}", Translation = "需要 <color=red>{0}</color> & 电力"
        };
        var document = new XmlDocument { XmlResolver = null };
        document.LoadXml(DefTranslationXml.WriteDefInjected(new[] { record }));
        Assert.Equal(record.Translation, document.DocumentElement["Windmill.description"].InnerText);
    }
}
