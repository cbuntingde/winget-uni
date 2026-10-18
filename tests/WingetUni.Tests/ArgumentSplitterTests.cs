using WingetUni.Core.Parsing;

namespace WingetUni.Tests;

public sealed class ArgumentSplitterTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ReturnsNothingForBlankInput(string? commandLine)
    {
        Assert.Empty(ArgumentSplitter.Split(commandLine));
    }

    [Fact]
    public void SplitsOnWhitespace()
    {
        Assert.Equal(["--silent", "--force"], ArgumentSplitter.Split("--silent   --force"));
    }

    [Fact]
    public void KeepsAQuotedValueTogether()
    {
        Assert.Equal(["--path", @"C:\Program Files\App"], ArgumentSplitter.Split(@"--path ""C:\Program Files\App"""));
    }

    [Fact]
    public void KeepsAnEmptyQuotedValue()
    {
        Assert.Equal(["--name", string.Empty], ArgumentSplitter.Split("--name \"\""));
    }

    [Fact]
    public void TreatsADoubledBackslashBeforeAQuoteAsOneLiteralBackslash()
    {
        // Two backslashes and a quote means a literal backslash followed by the closing quote.
        Assert.Equal([@"C:\dir\", "b"], ArgumentSplitter.Split(@"""C:\dir\\"" b"));
    }

    [Fact]
    public void EscapesAQuoteInsideAQuotedValue()
    {
        Assert.Equal(["a\"b"], ArgumentSplitter.Split(@"""a\""b"""));
    }

    [Fact]
    public void KeepsBackslashesThatDoNotPrecedeAQuote()
    {
        Assert.Equal([@"C:\a\b\c"], ArgumentSplitter.Split(@"C:\a\b\c"));
    }

    [Fact]
    public void JoinsQuotedAndUnquotedTextIntoOneArgument()
    {
        Assert.Equal(["--flag=value with space"], ArgumentSplitter.Split(@"--flag=""value with space"""));
    }
}
