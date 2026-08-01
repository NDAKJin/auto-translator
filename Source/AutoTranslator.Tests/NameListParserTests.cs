using System.Collections.Generic;
using Xunit;

namespace AutoTranslator.Tests
{
    public class NameListParserTests
    {
        [Fact]
        public void Parse_EmptyText_ReturnsEmptyDictionary()
        {
            var result = NameListParser.Parse("");
            Assert.Empty(result);
        }
    }
}
