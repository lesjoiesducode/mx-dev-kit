namespace MxDevKit.Tests;

public class DevExcusesTests
{
    public static TheoryData<string> Languages => new() { "en", "fr" };

    private static IReadOnlyList<string> List(string code) => code == "fr" ? DevExcuses.French : DevExcuses.English;

    [Theory]
    [MemberData(nameof(Languages))]
    public void About_forty_excuses_per_language(string code) =>
        Assert.InRange(List(code).Count, 35, 50);

    [Theory]
    [MemberData(nameof(Languages))]
    public void Excuses_are_unique_one_line_sentences(string code)
    {
        var list = List(code);
        Assert.Equal(list.Count, list.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var excuse in list)
        {
            Assert.Equal(excuse.Trim(), excuse);
            Assert.DoesNotContain('\n', excuse);
            Assert.InRange(excuse.Length, 5, 80);
            Assert.Contains(excuse[^1], ".?!");
        }
    }

    [Fact]
    public void Excuses_follow_the_language_of_the_plugin()
    {
        Assert.Same(DevExcuses.French, DevExcuses.For(Strings.French));
        Assert.Same(DevExcuses.English, DevExcuses.For(Strings.English));
        Assert.Same(DevExcuses.French, DevExcuses.For(Strings.For("auto", "fr")));
        Assert.Same(DevExcuses.English, DevExcuses.For(Strings.For("auto", "de")));
    }

    [Fact]
    public void Never_the_same_excuse_twice_in_a_row()
    {
        var picker = new ExcusePicker(new Random(42));
        var previous = picker.Next(DevExcuses.English);
        for (var i = 0; i < 2000; i++)
        {
            var next = picker.Next(DevExcuses.English);
            Assert.NotEqual(previous, next);
            previous = next;
        }
    }

    [Fact]
    public void Two_excuses_alternate()
    {
        var picker = new ExcusePicker(new Random(1));
        var pair = new[] { "a.", "b." };
        var first = picker.Next(pair);
        Assert.Equal(first == "a." ? "b." : "a.", picker.Next(pair));
        Assert.Equal(first, picker.Next(pair));
    }

    [Fact]
    public void Every_excuse_comes_up()
    {
        var picker = new ExcusePicker(new Random(7));
        var seen = new HashSet<string>();
        for (var i = 0; i < 2000; i++)
        {
            seen.Add(picker.Next(DevExcuses.French));
        }

        Assert.Equal(DevExcuses.French.Count, seen.Count);
    }

    [Fact]
    public void Switching_lists_keeps_working()
    {
        var picker = new ExcusePicker(new Random(3));
        Assert.Contains(picker.Next(DevExcuses.English), DevExcuses.English);
        Assert.Contains(picker.Next(DevExcuses.French), DevExcuses.French);
        Assert.Equal("only.", picker.Next(new[] { "only." }));
        Assert.Equal("only.", picker.Next(new[] { "only." }));
        Assert.Equal("", picker.Next(Array.Empty<string>()));
    }

    [Fact]
    public void Excuse_bubble_is_pink_with_a_smiley()
    {
        var bubble = Bubbles.Excuse(Strings.French);
        Assert.Equal(Glyph.Smiley, bubble.Glyph);
        Assert.Equal(Palette.Pink, bubble.Background);
        Assert.Equal(Rgb.Parse("#BF3989"), Palette.Pink);
        Assert.Equal("Excuses de dev", bubble.Label);
        Assert.Equal("Dev excuses", Bubbles.Excuse(Strings.English).Label);
        Assert.Equal("", bubble.Value);
    }
}
