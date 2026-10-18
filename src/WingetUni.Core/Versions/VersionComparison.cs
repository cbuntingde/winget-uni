using System.Globalization;
using System.Text;

namespace WingetUni.Core.Versions;

/// <summary>
/// Compares package versions that come from package managers rather than from one consistent
/// scheme. Versions are split into numeric and alphabetic tokens and compared token by token,
/// which orders the dotted numeric versions that Winget reports while still giving a stable
/// answer for arbitrary strings such as "Unknown".
/// </summary>
/// <remarks>
/// Three rules cover the cases that occur in practice:
/// <list type="bullet">
///   <item>digit tokens compare as numbers, so "9.0" is older than "10.0";</item>
///   <item>a version that runs out of digit tokens is padded with zero, so "1.2" equals
///   "1.2.0" and is older than "1.2.1";</item>
///   <item>an alphabetic remainder is a pre-release label, so "1.0.0-beta" is older than
///   "1.0.0".</item>
/// </list>
/// The comparison never throws and never returns an undefined result: two versions are either
/// ordered or equal. Versions that cannot be interpreted as numbers fall back to an ordinal,
/// case-insensitive comparison of their tokens instead of failing.
/// </remarks>
public static class VersionComparison
{
    /// <summary>Orders <paramref name="left"/> against <paramref name="right"/>.</summary>
    /// <returns>A negative value, zero, or a positive value, following <see cref="IComparable{T}"/> conventions.</returns>
    public static int Compare(string? left, string? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        var leftEmpty = string.IsNullOrWhiteSpace(left);
        var rightEmpty = string.IsNullOrWhiteSpace(right);

        if (leftEmpty || rightEmpty)
        {
            // A missing version sorts before anything concrete, including an empty one.
            return leftEmpty && rightEmpty ? 0 : leftEmpty ? -1 : 1;
        }

        var leftTokens = Tokenize(left!);
        var rightTokens = Tokenize(right!);

        if (leftTokens.Count == 0 || rightTokens.Count == 0)
        {
            // Input such as "<" leaves no tokens to compare.
            return string.Compare(left, right, StringComparison.OrdinalIgnoreCase) switch
            {
                < 0 => -1,
                > 0 => 1,
                _ => 0,
            };
        }

        var count = Math.Max(leftTokens.Count, rightTokens.Count);

        for (var index = 0; index < count; index++)
        {
            var leftToken = index < leftTokens.Count ? leftTokens[index] : null;
            var rightToken = index < rightTokens.Count ? rightTokens[index] : null;

            if (leftToken is null || rightToken is null)
            {
                var present = leftToken ?? rightToken!;

                if (!IsDigits(present))
                {
                    // An alphabetic remainder marks the version that has it as a pre-release,
                    // which precedes the release it belongs to.
                    return leftToken is null ? 1 : -1;
                }

                // A version that ran out of digit tokens is padded with zero, so the shorter
                // version continues as if it ended in zeros.
                leftToken ??= "0";
                rightToken ??= "0";
            }

            var comparison = IsDigits(leftToken) && IsDigits(rightToken)
                ? CompareDigits(leftToken, rightToken)
                : string.Compare(leftToken, rightToken, StringComparison.OrdinalIgnoreCase);

            if (comparison != 0)
            {
                return comparison < 0 ? -1 : 1;
            }
        }

        return 0;
    }

    /// <summary>
    /// Orders two digit-only tokens by value. Leading zeros are ignored and the comparison is
    /// done on length before digits, so a build number too long for a fixed-width integer is
    /// still ordered correctly.
    /// </summary>
    private static int CompareDigits(string left, string right)
    {
        var leftSignificant = left.TrimStart('0');
        var rightSignificant = right.TrimStart('0');

        if (leftSignificant.Length != rightSignificant.Length)
        {
            return leftSignificant.Length < rightSignificant.Length ? -1 : 1;
        }

        return string.CompareOrdinal(leftSignificant, rightSignificant) switch
        {
            < 0 => -1,
            > 0 => 1,
            _ => 0,
        };
    }

    private static bool IsDigits(string token)
    {
        if (token.Length == 0)
        {
            return false;
        }

        foreach (var character in token)
        {
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    private static List<string> Tokenize(string version)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();

        foreach (var character in version)
        {
            if (char.IsLetterOrDigit(character))
            {
                current.Append(character);
            }
            else if (current.Length > 0)
            {
                tokens.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }
}
