using System.Collections.Generic;

namespace ace_run.Models;

public class FolderItem : TreeItem
{
    public List<TreeItem> Children { get; set; } = new();
    public bool IsExpanded { get; set; } = true;
}
