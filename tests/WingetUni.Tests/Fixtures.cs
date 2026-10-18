namespace WingetUni.Tests;

/// <summary>
/// Loads output recorded from the real package manager tools.
/// </summary>
/// <remarks>
/// The parsers are checked against these recordings rather than against hand-written samples,
/// so a layout detail that only occurs in practice, such as an unpadded final column, is
/// covered by the tests.
/// </remarks>
internal static class Fixtures
{
    public static IReadOnlyList<string> ReadLines(string fileName) =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));
}
