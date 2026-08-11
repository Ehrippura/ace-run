using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace ace_run.Models;

public class AppData
{
    /// <summary>
    /// The shape this build writes. Bump it when a field is added.
    /// v5 added AppItem.Kind, v6 added AppItem.SortKey.
    /// </summary>
    public const int CurrentVersion = 6;

    /// <summary>
    /// Version of the file this instance came from, or <see cref="CurrentVersion"/> for a
    /// fresh one. <see cref="Services.DataService"/> re-stamps it on every write: the number
    /// describes the shape that was <em>written</em>, so a v3 file loaded and saved by this
    /// build is a v6 file. Nothing branches on it yet — it exists so a future migration can.
    /// </summary>
    public int Version { get; set; } = CurrentVersion;
    public List<TagItem> Tags { get; set; } = new();
    public List<AppItem> UngroupedItems { get; set; } = new();
    public List<FolderItem> Folders { get; set; } = new();
    public List<RecentLaunch> RecentLaunches { get; set; } = new();

    /// <summary>
    /// Every item in the workspace, ungrouped and foldered alike. This is the number
    /// <see cref="WorkspaceInfo.AppCount"/> denormalizes, and it was open-coded at four
    /// separate call sites before it lived here.
    /// </summary>
    /// <remarks>
    /// Not serialized: it is derived, and persisting it would create a second copy that
    /// could disagree with the lists it counts.
    /// </remarks>
    [JsonIgnore]
    public int ItemCount => UngroupedItems.Count + Folders.Sum(f => f.Children.Count);
}
