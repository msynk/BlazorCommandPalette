using System.Buffers;

namespace BlazorCommandPalette.Search;

/// <summary>
/// fzf-inspired fuzzy matcher using dynamic programming (Smith-Waterman style with affine gaps).
/// Rewards matches at word boundaries, camelCase humps, consecutive runs and the first pattern character;
/// penalizes gaps. Smart case: a lower-case query is case-insensitive, mixed case is case-sensitive.
/// Returns the positions of the optimal alignment for highlighting.
/// </summary>
public sealed class FuzzyMatcher : ICommandMatcher
{
    public const int ScoreMatch = 16;
    public const int ScoreGapStart = -3;
    public const int ScoreGapExtension = -1;
    public const int BonusBoundary = 8;
    public const int BonusNonWord = 8;
    public const int BonusCamel = 7;
    public const int BonusConsecutive = 4;
    public const int BonusFirstCharMultiplier = 2;
    public const int BonusCaseMatch = 1;

    /// <summary>Shared default instance.</summary>
    public static FuzzyMatcher Default { get; } = new();

    /// <summary>Disable smart case and always match case-insensitively.</summary>
    public bool IgnoreCase { get; init; }

    public FieldMatch Match(string query, string text)
    {
        if (string.IsNullOrEmpty(query))
        {
            return FieldMatch.Empty;
        }

        if (string.IsNullOrEmpty(text) || query.Length > text.Length)
        {
            return FieldMatch.None;
        }

        var caseSensitive = !IgnoreCase && query.Any(char.IsUpper);

        // Fast reject: must be a subsequence.
        if (!IsSubsequence(query, text, caseSensitive))
        {
            return FieldMatch.None;
        }

        int p = query.Length, t = text.Length;
        var pool = ArrayPool<int>.Shared;
        var size = (p + 1) * (t + 1);
        var m = pool.Rent(size);       // best score with query[i] matched at text[j]
        var d = pool.Rent(size);       // best score for query[..i] within text[..j]
        var consec = pool.Rent(size);  // length of consecutive run ending at (i, j)
        var bonus = pool.Rent(t);
        try
        {
            ComputeBonus(text, bonus);
            Array.Fill(m, int.MinValue / 2, 0, size);
            Array.Fill(d, int.MinValue / 2, 0, size);
            Array.Fill(consec, 0, 0, size);

            int Idx(int i, int j) => i * (t + 1) + j;

            for (var i = 0; i < p; i++)
            {
                var qc = query[i];
                var inGapPrev = false;
                for (var j = 0; j < t; j++)
                {
                    var tc = text[j];
                    var cur = Idx(i + 1, j + 1);
                    if (CharEquals(qc, tc, caseSensitive))
                    {
                        int prevScore;
                        var run = 0;
                        if (i == 0)
                        {
                            prevScore = 0;
                        }
                        else
                        {
                            prevScore = d[Idx(i, j)];
                            if (prevScore <= int.MinValue / 4)
                            {
                                m[cur] = int.MinValue / 2;
                                goto gap;
                            }

                            var prevMatch = m[Idx(i, j)];
                            if (prevMatch == prevScore)
                            {
                                run = consec[Idx(i, j)];
                            }
                        }

                        var b = bonus[j];
                        if (run > 0)
                        {
                            // Carry the bonus that started the run so "Foo" inside "fooBar" still benefits.
                            var runStart = j - run;
                            var startBonus = runStart >= 0 ? bonus[runStart] : 0;
                            b = Math.Max(Math.Max(b, BonusConsecutive), startBonus);
                        }

                        if (i == 0)
                        {
                            b *= BonusFirstCharMultiplier;
                        }

                        if (caseSensitive || (qc == tc && char.IsUpper(tc)))
                        {
                            b += BonusCaseMatch;
                        }

                        m[cur] = prevScore + ScoreMatch + b;
                        consec[cur] = run + 1;
                    }
                    else
                    {
                        m[cur] = int.MinValue / 2;
                    }

                gap:
                    var left = d[Idx(i + 1, j)];
                    var gapScore = left <= int.MinValue / 4 ? int.MinValue / 2 : left + (inGapPrev ? ScoreGapExtension : ScoreGapStart);
                    if (m[cur] >= gapScore)
                    {
                        d[cur] = m[cur];
                        inGapPrev = false;
                    }
                    else
                    {
                        d[cur] = gapScore;
                        inGapPrev = true;
                    }
                }
            }

            // Best end position for the last pattern char.
            var best = int.MinValue / 2;
            var bestJ = -1;
            for (var j = 0; j < t; j++)
            {
                var v = m[Idx(p, j + 1)];
                if (v > best)
                {
                    best = v;
                    bestJ = j;
                }
            }

            if (bestJ < 0 || best <= int.MinValue / 4)
            {
                return FieldMatch.None;
            }

            // Backtrace positions.
            var positions = new int[p];
            var jj = bestJ;
            for (var i = p - 1; i >= 0; i--)
            {
                positions[i] = jj;
                if (i == 0)
                {
                    break;
                }

                // Previous match ends at the j' <= jj-1 where d == m (the alignment came from a match, not a gap).
                jj--;
                while (jj >= 0 && d[Idx(i, jj + 1)] != m[Idx(i, jj + 1)])
                {
                    jj--;
                }
            }

            // Tie-breakers (fractions of one bonus point): prefer shorter texts and earlier matches.
            var score = best - 0.02 * t - 0.01 * positions[0];
            return new FieldMatch(true, score, positions);
        }
        finally
        {
            pool.Return(m);
            pool.Return(d);
            pool.Return(consec);
            pool.Return(bonus);
        }
    }

    private static bool CharEquals(char a, char b, bool caseSensitive)
        => caseSensitive ? a == b : char.ToLowerInvariant(a) == char.ToLowerInvariant(b);

    private static bool IsSubsequence(string query, string text, bool caseSensitive)
    {
        var qi = 0;
        for (var ti = 0; ti < text.Length && qi < query.Length; ti++)
        {
            if (CharEquals(query[qi], text[ti], caseSensitive))
            {
                qi++;
            }
        }

        return qi == query.Length;
    }

    private static void ComputeBonus(string text, int[] bonus)
    {
        var prev = CharClass.NonWord;
        for (var i = 0; i < text.Length; i++)
        {
            var cls = Classify(text[i]);
            bonus[i] = BonusFor(prev, cls);
            prev = cls;
        }
    }

    private enum CharClass { NonWord, Lower, Upper, Digit, Letter }

    private static CharClass Classify(char c)
    {
        if (char.IsLower(c)) return CharClass.Lower;
        if (char.IsUpper(c)) return CharClass.Upper;
        if (char.IsDigit(c)) return CharClass.Digit;
        if (char.IsLetter(c)) return CharClass.Letter;
        return CharClass.NonWord;
    }

    private static int BonusFor(CharClass prev, CharClass cur)
    {
        if (prev == CharClass.NonWord && cur != CharClass.NonWord)
        {
            return BonusBoundary; // start of word (after space, punctuation, or at index 0)
        }

        if (prev == CharClass.Lower && cur == CharClass.Upper)
        {
            return BonusCamel; // camelCase hump
        }

        if (prev != CharClass.Digit && cur == CharClass.Digit)
        {
            return BonusCamel; // letter→digit transition
        }

        if (cur == CharClass.NonWord)
        {
            return BonusNonWord;
        }

        return 0;
    }
}

/// <summary>Simple case-insensitive substring matcher. Faster, no typo tolerance.</summary>
public sealed class SubstringMatcher : ICommandMatcher
{
    public static SubstringMatcher Default { get; } = new();

    public FieldMatch Match(string query, string text)
    {
        if (string.IsNullOrEmpty(query))
        {
            return FieldMatch.Empty;
        }

        var idx = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return FieldMatch.None;
        }

        var score = query.Length * FuzzyMatcher.ScoreMatch + (idx == 0 ? FuzzyMatcher.BonusBoundary * 2 : 0);
        return new FieldMatch(true, score, Enumerable.Range(idx, query.Length).ToArray());
    }
}
