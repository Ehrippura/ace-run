namespace ace_run.Models;

/// <summary>
/// What an <see cref="AppItem"/> points at. Serialized as a string ("App" / "Url");
/// absent in pre-v5 workspace files, which correctly falls back to <see cref="App"/>.
/// </summary>
public enum ItemKind
{
    /// <summary>An executable on disk. Uses Arguments, WorkingDirectory and RunAsAdmin.</summary>
    App,

    /// <summary>A URL or custom protocol handled by the shell. Ignores the exe-only fields.</summary>
    Url
}
