using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace BlazorCommandPalette;

/// <summary>
/// A keyboard shortcut: one or more chords pressed in sequence (<c>Mod+K</c>, <c>Ctrl+Shift+P</c>, <c>G I</c>).
/// <c>Mod</c> maps to ⌘ on macOS and Ctrl elsewhere.
/// </summary>
public sealed record KeyboardShortcut
{
    public KeyboardShortcut(IReadOnlyList<KeyChord> chords)
    {
        if (chords.Count == 0)
        {
            throw new ArgumentException("A shortcut needs at least one chord.", nameof(chords));
        }

        Chords = chords;
    }

    public IReadOnlyList<KeyChord> Chords { get; }

    /// <summary>True for multi-chord sequences such as <c>G I</c>.</summary>
    public bool IsSequence => Chords.Count > 1;

    /// <summary>Parse a shortcut string. Chords are separated by spaces, keys inside a chord by <c>+</c>.</summary>
    public static KeyboardShortcut Parse(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var chords = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(KeyChord.Parse)
            .ToArray();
        return new KeyboardShortcut(chords);
    }

    public static bool TryParse(string? text, [NotNullWhen(true)] out KeyboardShortcut? shortcut)
    {
        shortcut = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            shortcut = Parse(text);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Canonical lower-case form used for matching, e.g. <c>mod+k</c> or <c>g i</c>.</summary>
    public string Normalized => string.Join(' ', Chords.Select(c => c.Normalized));

    /// <summary>Platform-specific display tokens per chord, e.g. [["⌘","K"]] or [["Ctrl","K"]].</summary>
    public IReadOnlyList<IReadOnlyList<string>> DisplayTokens(bool isMac) => Chords.Select(c => c.DisplayTokens(isMac)).ToArray();

    /// <summary>Human readable text, e.g. <c>Ctrl+K</c> or <c>G then I</c>.</summary>
    public string ToDisplayString(bool isMac) => string.Join(isMac ? " " : " then ", DisplayTokens(isMac).Select(t => string.Join(isMac ? string.Empty : "+", t)));

    public bool Equals(KeyboardShortcut? other) => other is not null && Normalized == other.Normalized;

    public override int GetHashCode() => Normalized.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => Normalized;

    public static implicit operator KeyboardShortcut(string text) => Parse(text);
}

/// <summary>A single key press with modifiers.</summary>
public sealed record KeyChord(string Key, bool Ctrl = false, bool Shift = false, bool Alt = false, bool Meta = false, bool Mod = false)
{
    private static readonly Dictionary<string, string> KeyAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["esc"] = "escape",
        ["return"] = "enter",
        ["space"] = " ",
        ["spacebar"] = " ",
        ["del"] = "delete",
        ["ins"] = "insert",
        ["up"] = "arrowup",
        ["down"] = "arrowdown",
        ["left"] = "arrowleft",
        ["right"] = "arrowright",
        ["plus"] = "+",
        ["comma"] = ",",
        ["period"] = ".",
        ["slash"] = "/",
        ["backslash"] = "\\",
        ["minus"] = "-",
        ["pgup"] = "pageup",
        ["pgdn"] = "pagedown",
    };

    public static KeyChord Parse(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        bool ctrl = false, shift = false, alt = false, meta = false, mod = false;
        string? key = null;

        IEnumerable<string> parts = text.EndsWith("++", StringComparison.Ordinal)
            ? text[..^2].Split('+', StringSplitOptions.RemoveEmptyEntries).Append("+")
            : text.Split('+', StringSplitOptions.RemoveEmptyEntries);

        foreach (var raw in parts)
        {
            var p = raw.Trim();
            switch (p.ToLowerInvariant())
            {
                case "ctrl" or "control" or "⌃": ctrl = true; break;
                case "shift" or "⇧": shift = true; break;
                case "alt" or "option" or "opt" or "⌥": alt = true; break;
                case "meta" or "cmd" or "command" or "win" or "super" or "⌘": meta = true; break;
                case "mod" or "cmdorctrl" or "commandorcontrol": mod = true; break;
                default:
                    if (key is not null)
                    {
                        throw new FormatException($"Chord '{text}' has more than one non-modifier key.");
                    }

                    key = KeyAliases.TryGetValue(p, out var alias) ? alias : p.ToLowerInvariant();
                    break;
            }
        }

        if (key is null)
        {
            throw new FormatException($"Chord '{text}' has no key.");
        }

        return new KeyChord(key, ctrl, shift, alt, meta, mod);
    }

    /// <summary>Canonical lower-case form, modifiers in fixed order: mod, ctrl, alt, shift, meta.</summary>
    public string Normalized
    {
        get
        {
            var sb = new StringBuilder();
            if (Mod) sb.Append("mod+");
            if (Ctrl) sb.Append("ctrl+");
            if (Alt) sb.Append("alt+");
            if (Shift) sb.Append("shift+");
            if (Meta) sb.Append("meta+");
            sb.Append(Key);
            return sb.ToString();
        }
    }

    public IReadOnlyList<string> DisplayTokens(bool isMac)
    {
        var tokens = new List<string>(5);
        if (Mod) tokens.Add(isMac ? "⌘" : "Ctrl");
        if (Ctrl) tokens.Add(isMac ? "⌃" : "Ctrl");
        if (Alt) tokens.Add(isMac ? "⌥" : "Alt");
        if (Shift) tokens.Add(isMac ? "⇧" : "Shift");
        if (Meta) tokens.Add(isMac ? "⌘" : "Win");
        tokens.Add(DisplayKey(Key, isMac));
        return tokens;
    }

    private static string DisplayKey(string key, bool isMac) => key switch
    {
        "enter" => isMac ? "↩" : "Enter",
        "escape" => "Esc",
        "backspace" => isMac ? "⌫" : "Backspace",
        "delete" => isMac ? "⌦" : "Del",
        "tab" => isMac ? "⇥" : "Tab",
        " " => "Space",
        "arrowup" => "↑",
        "arrowdown" => "↓",
        "arrowleft" => "←",
        "arrowright" => "→",
        "pageup" => "PgUp",
        "pagedown" => "PgDn",
        _ when key.Length == 1 => key.ToUpperInvariant(),
        _ => char.ToUpperInvariant(key[0]) + key[1..],
    };
}
