using System;

namespace ace_run.Models;

/// <remarks>
/// Implements <see cref="ITagRef"/> so the persisted tag can go through the same
/// <see cref="Services.TagOrdering"/> helpers as the app's <c>TagViewModel</c> — the workspace
/// merge orders an imported item's tags with it, and there is no view model at that point.
/// </remarks>
public class TagItem : ITagRef
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string ColorKey { get; set; } = "Blue";
}
