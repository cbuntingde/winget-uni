using System.Windows;

namespace WingetUni.App.Theming;

/// <summary>Swaps the colour palette that the control styles draw from.</summary>
/// <remarks>
/// Each theme dictionary defines the same set of brush keys, and the control styles refer to them
/// with <c>DynamicResource</c>, so replacing one dictionary restyles the whole window live.
/// </remarks>
public static class ThemeManager
{
    public const string Dark = "Dark";
    public const string Light = "Light";

    public static string Current { get; private set; } = Dark;

    public static bool IsLight => string.Equals(Current, Light, StringComparison.Ordinal);

    public static void Apply(string? theme)
    {
        var name = string.Equals(theme, Light, StringComparison.OrdinalIgnoreCase) ? Light : Dark;

        var application = Application.Current;
        if (application is null)
        {
            Current = name;
            return;
        }

        var dictionaries = application.Resources.MergedDictionaries;
        var replacement = new ResourceDictionary { Source = new Uri($"Themes/{name}.xaml", UriKind.Relative) };

        var existing = dictionaries.FirstOrDefault(dictionary => IsThemeDictionary(dictionary.Source));
        if (existing is null)
        {
            dictionaries.Insert(0, replacement);
        }
        else
        {
            var index = dictionaries.IndexOf(existing);
            dictionaries.RemoveAt(index);
            dictionaries.Insert(index, replacement);
        }

        Current = name;
    }

    private static bool IsThemeDictionary(Uri? source)
    {
        var text = source?.OriginalString;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        return text.EndsWith("Dark.xaml", StringComparison.OrdinalIgnoreCase)
            || text.EndsWith("Light.xaml", StringComparison.OrdinalIgnoreCase);
    }
}
