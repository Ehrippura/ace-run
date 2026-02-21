using System;

namespace ace_run.Models;

public class WorkspaceInfo
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastModifiedAt { get; set; } = DateTime.UtcNow;
    public string? ColorTag { get; set; }  // "Blue","Green","Red","Yellow","Purple" or null
    public int AppCount { get; set; }      // denormalized, updated on save
}
