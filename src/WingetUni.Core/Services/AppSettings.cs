namespace WingetUni.Core.Services;

/// <summary>User preferences, persisted between runs as JSON.</summary>
public sealed record AppSettings
{
    /// <summary>
    /// The value of <see cref="DiscoverManagerId"/> that means "search every available manager"
    /// rather than one of them. It cannot collide with a manager, whose identifier is never
    /// empty and never starts with an asterisk.
    /// </summary>
    public const string AllManagers = "*";

    /// <summary>"Dark" or "Light".</summary>
    public string Theme { get; init; } = "Dark";

    /// <summary>Check for available updates once, shortly after startup.</summary>
    public bool CheckForUpdatesOnStartup { get; init; } = true;

    /// <summary>Show a tray notification when updates are available.</summary>
    public bool NotifyWhenUpdatesAreAvailable { get; init; } = true;

    /// <summary>
    /// Whether the window's close button hides the application in the notification area instead
    /// of ending it. On by default, so a running install is not interrupted by a stray click.
    /// </summary>
    public bool MinimizeToTrayOnClose { get; init; } = true;

    /// <summary>
    /// How many results to request from each manager per search. Kept small by default because
    /// every manager runs its own process to answer and a search costs seconds, not milliseconds.
    /// </summary>
    public int SearchResultLimit { get; init; } = 25;

    /// <summary>Managers the user switched off. They are neither queried nor shown.</summary>
    public IReadOnlyList<string> DisabledManagers { get; init; } = [];

    /// <summary>
    /// The manager Discover searches, or <see cref="AllManagers"/> to search all of them. A null
    /// value means the user has never chosen, and the primary manager is used: a Winget package
    /// is what someone searching this application is normally after, and running every manager
    /// on the machine costs a process and several seconds each.
    /// </summary>
    public string? DiscoverManagerId { get; init; }

    /// <summary>Default install scope, or null to let each manager decide.</summary>
    public string? DefaultScope { get; init; }

    /// <summary>Whether the install dialog offers a silent install by default.</summary>
    public bool SilentByDefault { get; init; } = true;

    public bool IsManagerEnabled(string managerId) => !DisabledManagers.Contains(managerId);

    /// <summary>
    /// The manager a search should use: the saved one, or <paramref name="primaryManagerId"/>
    /// when the user has never chosen.
    /// </summary>
    /// <param name="primaryManagerId">
    /// The manager to fall back on, or null when there is nothing to fall back on.
    /// </param>
    /// <returns>The manager to search, or null to search every available one.</returns>
    public string? ResolveDiscoverManagerId(string? primaryManagerId) => DiscoverManagerId switch
    {
        // Never chosen: a person searching this application almost always wants the primary
        // manager, and asking every other one costs a process each.
        null => primaryManagerId,
        AllManagers => null,
        var id => id,
    };

    public AppSettings WithManagerEnabled(string managerId, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(managerId);

        var disabled = DisabledManagers.Where(id => !string.Equals(id, managerId, StringComparison.Ordinal)).ToList();

        if (!enabled)
        {
            disabled.Add(managerId);
        }

        return this with { DisabledManagers = disabled };
    }
}
