using System;

namespace ace_run.Models;

/// <summary>
/// One entry in the recent-launch list: which item was launched, and what to call it in the
/// tray menu.
/// </summary>
/// <remarks>
/// Deliberately not a copy of the item. The launch path used to be stored here as well, and
/// the tray menu started <em>that</em> — which dropped the arguments, the working directory
/// and the admin verb, so an item needing any of them behaved differently depending on where
/// it was clicked. The id is resolved back to the live item instead
/// (<c>MainWindow.LaunchRecent</c>); <see cref="Services.RecentLaunchList.Purge"/> is what
/// guarantees it still resolves.
/// </remarks>
public class RecentLaunch
{
    public Guid AppId { get; set; }

    /// <summary>
    /// The tray menu label, snapshotted so the menu can be built from this list alone. A
    /// rename shows up here on the item's next launch.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;
}
