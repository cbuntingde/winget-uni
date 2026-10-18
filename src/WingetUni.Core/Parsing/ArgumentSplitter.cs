using System.Text;

namespace WingetUni.Core.Parsing;

/// <summary>
/// Splits a command line typed by a user into individual arguments, following the quoting
/// rules that <c>CreateProcess</c> uses on Windows.
/// </summary>
/// <remarks>
/// This exists so that extra arguments entered in the interface can be passed to a package
/// manager as separate, correctly escaped arguments. Joining a user string onto a command
/// line by hand would break on any quoted value that contains spaces, such as an install path.
/// </remarks>
public static class ArgumentSplitter
{
    /// <summary>Splits <paramref name="commandLine"/>, returning an empty list for blank input.</summary>
    public static IReadOnlyList<string> Split(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return [];
        }

        var arguments = new List<string>();
        var argument = new StringBuilder();
        var insideQuotes = false;
        var hasContent = false;
        var index = 0;

        while (index < commandLine.Length)
        {
            var character = commandLine[index];

            if (character == '\\')
            {
                // Backslashes are literal unless they immediately precede a quote, where
                // every pair becomes one backslash and an odd one escapes the quote.
                var backslashes = 0;
                while (index < commandLine.Length && commandLine[index] == '\\')
                {
                    backslashes++;
                    index++;
                }

                if (index < commandLine.Length && commandLine[index] == '"')
                {
                    argument.Append('\\', backslashes / 2);
                    if (backslashes % 2 == 0)
                    {
                        insideQuotes = !insideQuotes;
                    }
                    else
                    {
                        argument.Append('"');
                    }

                    hasContent = true;
                    index++;
                    continue;
                }

                argument.Append('\\', backslashes);
                hasContent = true;
                continue;
            }

            if (character == '"')
            {
                insideQuotes = !insideQuotes;
                hasContent = true;
                index++;
                continue;
            }

            if (!insideQuotes && char.IsWhiteSpace(character))
            {
                if (hasContent)
                {
                    arguments.Add(argument.ToString());
                    argument.Clear();
                    hasContent = false;
                }

                index++;
                continue;
            }

            argument.Append(character);
            hasContent = true;
            index++;
        }

        if (hasContent)
        {
            arguments.Add(argument.ToString());
        }

        return arguments;
    }
}
