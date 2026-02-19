namespace ace_run.Models;

public class AppItem : TreeItem
{
    public string FilePath { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public bool RunAsAdmin { get; set; }
    public string CustomIconPath { get; set; } = string.Empty;
}
