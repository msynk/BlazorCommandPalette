namespace BlazorCommandPalette.Tests;

public class KeyboardShortcutTests
{
    [Theory]
    [InlineData("Mod+K", "mod+k")]
    [InlineData("ctrl+shift+p", "ctrl+shift+p")]
    [InlineData("Shift+Ctrl+P", "ctrl+shift+p")]
    [InlineData("Cmd+Enter", "meta+enter")]
    [InlineData("G I", "g i")]
    [InlineData("g  then", "g then")]
    [InlineData("Ctrl++", "ctrl++")]
    [InlineData("Alt+Up", "alt+arrowup")]
    [InlineData("Esc", "escape")]
    public void Parses_and_normalizes(string input, string expected)
    {
        Assert.Equal(expected, KeyboardShortcut.Parse(input).Normalized);
    }

    [Fact]
    public void Rejects_chord_with_two_keys()
    {
        Assert.Throws<FormatException>(() => KeyboardShortcut.Parse("a+b"));
    }

    [Fact]
    public void Rejects_modifier_only_chord()
    {
        Assert.Throws<FormatException>(() => KeyboardShortcut.Parse("ctrl+shift"));
    }

    [Fact]
    public void TryParse_returns_false_on_garbage()
    {
        Assert.False(KeyboardShortcut.TryParse("ctrl+", out _));
        Assert.False(KeyboardShortcut.TryParse("", out _));
        Assert.True(KeyboardShortcut.TryParse("mod+k", out var s));
        Assert.Equal("mod+k", s!.Normalized);
    }

    [Fact]
    public void Display_tokens_are_platform_aware()
    {
        var s = KeyboardShortcut.Parse("Mod+Shift+K");
        Assert.Equal(new[] { "⌘", "⇧", "K" }, s.DisplayTokens(isMac: true)[0]);
        Assert.Equal(new[] { "Ctrl", "Shift", "K" }, s.DisplayTokens(isMac: false)[0]);
    }

    [Fact]
    public void Sequences_display_with_then()
    {
        var s = KeyboardShortcut.Parse("G I");
        Assert.True(s.IsSequence);
        Assert.Equal("G then I", s.ToDisplayString(isMac: false));
    }

    [Fact]
    public void Equality_is_by_normalized_form()
    {
        Assert.Equal(KeyboardShortcut.Parse("Shift+Ctrl+P"), KeyboardShortcut.Parse("ctrl+shift+p"));
        Assert.Equal(KeyboardShortcut.Parse("mod+k").GetHashCode(), KeyboardShortcut.Parse("Mod+K").GetHashCode());
    }

    [Fact]
    public void Implicit_conversion_from_string()
    {
        KeyboardShortcut s = "Ctrl+B";
        Assert.Equal("ctrl+b", s.Normalized);
    }
}
