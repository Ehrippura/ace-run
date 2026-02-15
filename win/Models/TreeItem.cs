using System;
using System.Text.Json.Serialization;

namespace ace_run.Models;

[JsonDerivedType(typeof(AppItem), "app")]
[JsonDerivedType(typeof(FolderItem), "folder")]
public abstract class TreeItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DisplayName { get; set; } = string.Empty;
}
