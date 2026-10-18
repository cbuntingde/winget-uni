using WingetUni.Core.Parsing;

namespace WingetUni.Tests;

public sealed class DisplayWidthTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("Git", 3)]
    [InlineData("视觉效果测试", 12)]
    [InlineData("中文Git", 7)]
    [InlineData("ｆｕｌｌ", 8)]
    public void MeasuresTextInTerminalColumns(string text, int expected)
    {
        Assert.Equal(expected, DisplayWidth.Measure(text));
    }

    [Fact]
    public void GivesACombiningMarkNoWidth()
    {
        // "e" followed by a combining acute accent occupies one column.
        Assert.Equal(1, DisplayWidth.Measure("e\u0301"));
    }

    [Fact]
    public void GivesAnEmojiTwoColumns()
    {
        Assert.Equal(2, DisplayWidth.Measure("🙂"));
    }
}
