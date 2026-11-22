namespace NexaDock.Core.Models;

/// <summary>A launchable dock item.</summary>
/// <param name="Id">Stable unique identifier.</param>
/// <param name="Name">Display name shown in tooltips.</param>
/// <param name="Target">File path, <c>shell:AppsFolder\AUMID</c> or URI understood by the shell.</param>
public sealed record DockShortcut(
    string Id,
    string Name,
    string Target,
    string? Arguments = null,
    string? WorkingDirectory = null)
{
    public static DockShortcut Create(string name, string target, string? arguments = null) =>
        new(Guid.NewGuid().ToString("N"), name, target, arguments);
}
