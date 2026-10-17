using BlazorCommandPalette.Search;

namespace BlazorCommandPalette.Tests;

public class FuzzyMatcherTests
{
    private readonly FuzzyMatcher _m = new();

    [Theory]
    [InlineData("gs", "Go to Settings")]
    [InlineData("settings", "Go to Settings")]
    [InlineData("gts", "Go to Settings")]
    [InlineData("tgl", "Toggle dark mode")]
    public void Matches_subsequences(string query, string text)
    {
        var r = _m.Match(query, text);
        Assert.True(r.Matched);
        Assert.Equal(query.Length, r.Positions.Length);
    }

    [Theory]
    [InlineData("xyz", "Go to Settings")]
    [InlineData("settingsx", "Settings")]
    [InlineData("longer than text", "short")]
    public void Rejects_non_matches(string query, string text)
    {
        Assert.False(_m.Match(query, text).Matched);
    }

    [Fact]
    public void Empty_query_matches_everything_with_zero_score()
    {
        var r = _m.Match(string.Empty, "Anything");
        Assert.True(r.Matched);
        Assert.Equal(0, r.Score);
    }

    [Fact]
    public void Prefers_word_boundaries_over_mid_word()
    {
        var boundary = _m.Match("op", "Open Project");   // O...P at word starts
        var mid = _m.Match("op", "Scoop it");             // o,p inside a word
        Assert.True(boundary.Score > mid.Score);
    }

    [Fact]
    public void Prefers_consecutive_runs_over_gaps()
    {
        var consecutive = _m.Match("set", "Settings");
        var gapped = _m.Match("set", "Sort elements today");
        Assert.True(consecutive.Score > gapped.Score);
    }

    [Fact]
    public void Rewards_camelCase_humps()
    {
        var camel = _m.Match("fb", "fooBar");
        var plain = _m.Match("fb", "foobar");
        Assert.True(camel.Score > plain.Score);
    }

    [Fact]
    public void Positions_point_at_matched_characters()
    {
        var r = _m.Match("gs", "Go to Settings");
        Assert.Equal('G', "Go to Settings"[r.Positions[0]]);
        Assert.Equal('S', "Go to Settings"[r.Positions[1]]);
        Assert.Equal(new[] { 0, 6 }, r.Positions);
    }

    [Fact]
    public void Positions_are_strictly_increasing()
    {
        var text = "Toggle the sidebar visibility";
        var r = _m.Match("tsv", text);
        Assert.True(r.Matched);
        for (var i = 1; i < r.Positions.Length; i++)
        {
            Assert.True(r.Positions[i] > r.Positions[i - 1]);
        }
    }

    [Fact]
    public void Smart_case_lowercase_query_is_case_insensitive()
    {
        Assert.True(_m.Match("open", "OPEN FILE").Matched);
    }

    [Fact]
    public void Smart_case_uppercase_query_is_case_sensitive()
    {
        Assert.False(_m.Match("OPEN", "open file").Matched);
        Assert.True(_m.Match("OPEN", "OPEN file").Matched);
    }

    [Fact]
    public void Exact_prefix_beats_scattered_match_in_ranking()
    {
        var cmds = new[]
        {
            Cmd.New("Preferences: Open Settings").Build(),
            Cmd.New("Settings").Build(),
            Cmd.New("Set Timer").Build(),
        };
        var ranked = cmds.Select(c => _m.Match(c, "settings")).Where(m => m is not null).OrderByDescending(m => m!.Score).ToList();
        Assert.Equal("Settings", ranked[0]!.Command.Title);
    }

    [Fact]
    public void Substring_matcher_matches_contiguously_only()
    {
        var s = SubstringMatcher.Default;
        Assert.True(s.Match("set", "Settings").Matched);
        Assert.False(s.Match("sts", "Settings").Matched);
    }
}

public class CommandMatcherExtensionsTests
{
    private readonly FuzzyMatcher _m = new();

    [Fact]
    public void Matches_keywords_and_reports_which_one()
    {
        var cmd = Cmd.New("Toggle theme").Keywords("dark", "light", "appearance").Build();
        var r = _m.Match(cmd, "appear");
        Assert.NotNull(r);
        Assert.Equal("appearance", r!.MatchedKeyword);
    }

    [Fact]
    public void Title_outranks_keyword_match()
    {
        var titleHit = Cmd.New("Dark mode").Build();
        var keywordHit = Cmd.New("Appearance").Keywords("dark mode").Build();
        var a = _m.Match(titleHit, "dark")!;
        var b = _m.Match(keywordHit, "dark")!;
        Assert.True(a.Score > b.Score);
        Assert.NotEmpty(a.TitlePositions);
        Assert.Empty(b.TitlePositions);
    }

    [Fact]
    public void Multi_term_query_requires_all_terms()
    {
        var cmd = Cmd.New("Open recent project").Build();
        Assert.NotNull(_m.Match(cmd, "open proj"));
        Assert.Null(_m.Match(cmd, "open banana"));
    }

    [Fact]
    public void Negation_operator_excludes()
    {
        var cmd = Cmd.New("Delete workspace").Build();
        Assert.Null(_m.Match(cmd, "work !delete"));
        Assert.NotNull(_m.Match(cmd, "work !rename"));
    }

    [Fact]
    public void Exact_prefix_suffix_operators()
    {
        var cmd = Cmd.New("Go to Settings").Build();
        Assert.NotNull(_m.Match(cmd, "'to set"));
        Assert.Null(_m.Match(cmd, "'tst"));
        Assert.NotNull(_m.Match(cmd, "^go"));
        Assert.Null(_m.Match(cmd, "^set"));
        Assert.NotNull(_m.Match(cmd, "ings$"));
        Assert.Null(_m.Match(cmd, "go$"));
    }

    [Fact]
    public void Subtitle_positions_are_reported()
    {
        var cmd = Cmd.New("Invoice 42").Subtitle("Acme Corporation").Build();
        var r = _m.Match(cmd, "acme")!;
        Assert.Equal(new[] { 0, 1, 2, 3 }, r.SubtitlePositions);
    }
}
