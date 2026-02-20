using System;

namespace ace_run.Models;

public class AppItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DisplayName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public bool RunAsAdmin { get; set; }
    public string CustomIconPath { get; set; } = string.Empty;
}
