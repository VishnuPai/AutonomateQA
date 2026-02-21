using Xunit;
using UiTestRunner.Constants;

namespace UiTestRunner.Tests.Constants
{
    public class ConstantsTests
    {
        [Fact]
        public void ActionTypes_ContainsExpectedValues()
        {
            // Assert
            Assert.Equal("click", ActionTypes.Click);
            Assert.Equal("fill", ActionTypes.Fill);
            Assert.Equal("type", ActionTypes.Type);
            Assert.Equal("input", ActionTypes.Input);
            Assert.Equal("check", ActionTypes.Check);
            Assert.Equal("uncheck", ActionTypes.Uncheck);
            Assert.Equal("navigate", ActionTypes.Navigate);
            Assert.Equal("goto", ActionTypes.Goto);
            Assert.Equal("hover", ActionTypes.Hover);
        }

        [Fact]
        public void SelectorTypes_ContainsExpectedValues()
        {
            // Assert
            Assert.Equal("Text", SelectorTypes.Text);
            Assert.Equal("Label", SelectorTypes.Label);
            Assert.Equal("Placeholder", SelectorTypes.Placeholder);
            Assert.Equal("CSS", SelectorTypes.Css);
        }

        [Fact]
        public void GherkinKeywords_ContainsExpectedValues()
        {
            // Assert
            Assert.Equal("When", GherkinKeywords.When);
            Assert.Equal("Then", GherkinKeywords.Then);
            Assert.Equal("And", GherkinKeywords.And);
            Assert.Equal("But", GherkinKeywords.But);
            Assert.Equal("Given", GherkinKeywords.Given);
            Assert.Equal("#", GherkinKeywords.Comment);
        }

        [Fact]
        public void VerificationResults_ContainsExpectedValues()
        {
            // Assert
            Assert.Equal("TRUE", VerificationResults.True);
            Assert.Equal("FALSE", VerificationResults.False);
        }
    }
}
