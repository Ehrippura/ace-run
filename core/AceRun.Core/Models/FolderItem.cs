using System;
using System.Collections.Generic;

namespace ace_run.Models;

public class FolderItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DisplayName { get; set; } = string.Empty;
    public List<AppItem> Children { get; set; } = new();
}
