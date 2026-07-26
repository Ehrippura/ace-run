using System;
using System.Collections.Generic;

namespace ace_run.Models;

public class AppItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ItemKind Kind { get; set; } = ItemKind.App;
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Path to the .exe when <see cref="Kind"/> is App, or the URL when it is Url.</summary>
    public string FilePath { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public bool RunAsAdmin { get; set; }
    public string CustomIconPath { get; set; } = string.Empty;
    public List<Guid> TagIds { get; set; } = new();
}
