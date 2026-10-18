namespace WingetUni.Providers;

/// <summary>
/// Finds an executable through the PATH and returns its full path, the way the operating system
/// would resolve a bare name.
/// </summary>
/// <remarks>
/// This exists for tools that are installed as a batch shim rather than as a program, npm and
/// Scoop being the two here. When such a shim is started by its bare name, the batch variable
/// <c>%~dp0</c> that the shim uses to locate its own files resolves to the working directory
/// instead of the shim's own folder, and the tool aborts with an error about a missing file in
/// the current directory. Starting it by its full path keeps <c>%~dp0</c> correct.
///
/// A name that cannot be found is returned unchanged, so the process runner still reports the
/// usual "could not be started" reason.
/// </remarks>
internal static class ExecutableLocator
{
    /// <summary>Extensions tried when the PATH does not define its own list.</summary>
    private static readonly string[] FallbackExtensions = [".COM", ".EXE", ".BAT", ".CMD"];

    public static string Resolve(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        // A name that already carries a directory is left alone: it is either absolute or
        // deliberately relative to the working directory.
        if (name.Contains(Path.DirectorySeparatorChar) || name.Contains(Path.AltDirectorySeparatorChar))
        {
            return name;
        }

        var searchPath = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(searchPath))
        {
            return name;
        }

        var extensions = ExtensionsFor(name);
        var directories = searchPath.Split(
            Path.PathSeparator,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var directory in directories)
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory, name + extension);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return name;
    }

    private static IReadOnlyList<string> ExtensionsFor(string name)
    {
        // A name that already ends in an extension is tried exactly, and nothing is added.
        if (Path.HasExtension(name))
        {
            return [string.Empty];
        }

        var extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (extensions.Count == 0)
        {
            extensions.AddRange(FallbackExtensions);
        }

        // A file with no extension at all is still worth trying, but only after the usual ones.
        extensions.Add(string.Empty);

        return extensions;
    }
}
