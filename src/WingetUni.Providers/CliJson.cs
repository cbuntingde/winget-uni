using System.Text.Json;

namespace WingetUni.Providers;

/// <summary>
/// Helpers for the tools that print JSON, in the form each of them produces.
/// </summary>
/// <remarks>
/// Two details have to be absorbed here. npm prints a blank line before its JSON on some
/// commands, and PowerShell's <c>ConvertTo-Json</c> emits a bare object rather than an array
/// when a pipeline yields a single result. <see cref="Select"/> therefore accepts either root
/// shape, so a provider does not have to care which one it received.
/// </remarks>
internal static class CliJson
{
    /// <summary>Index of the first '[' or '{' in the text, or -1 when it holds no JSON.</summary>
    public static int Start(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] is '[' or '{')
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>Parses the JSON that begins at the first bracket, or null when there is none.</summary>
    public static JsonDocument? Parse(string text)
    {
        var start = Start(text);
        return start < 0 ? null : JsonDocument.Parse(text[start..]);
    }

    /// <summary>
    /// Projects the JSON in <paramref name="text"/>, accepting either an array of items or a
    /// single item.
    /// </summary>
    /// <remarks>
    /// The selector must read what it needs from the element there and then. The element is
    /// backed by a document that is disposed as this method returns, so returning the element
    /// itself, or holding it past the call, would give a disposed value.
    /// </remarks>
    public static IReadOnlyList<T> Select<T>(string text, Func<JsonElement, T> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        using var document = Parse(text);
        if (document is null)
        {
            return [];
        }

        var results = new List<T>();

        switch (document.RootElement.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var element in document.RootElement.EnumerateArray())
                {
                    results.Add(selector(element));
                }

                break;

            case JsonValueKind.Object:
                results.Add(selector(document.RootElement));
                break;
        }

        return results;
    }

    /// <summary>Returns the named string, or null when the name is absent or is not a string.</summary>
    public static string? String(JsonElement element, string name) =>
        TryGet(element, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// Returns the named value as text, accepting a plain string or an object with a "url"
    /// property, which is the two shapes npm uses for a homepage or a repository.
    /// </summary>
    public static string? Text(JsonElement element, string name)
    {
        if (!TryGet(element, name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Object when value.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String =>
                url.GetString(),
            _ => null,
        };
    }

    /// <summary>
    /// Returns the named person as text, accepting a plain string or an object with a "name",
    /// which is the two shapes npm uses for an author.
    /// </summary>
    public static string? Person(JsonElement element, string name)
    {
        if (!TryGet(element, name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        return value.ValueKind == JsonValueKind.Object ? String(value, "name") : null;
    }

    /// <summary>Returns the named array of strings, or an empty list when it is absent.</summary>
    public static IReadOnlyList<string> Strings(JsonElement element, string name)
    {
        if (!TryGet(element, name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var results = new List<string>();

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { } text)
            {
                results.Add(text);
            }
        }

        return results;
    }

    private static bool TryGet(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            return element.TryGetProperty(name, out value);
        }

        value = default;
        return false;
    }
}
