using System;

namespace ace_run.Models;

public class TagItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string ColorKey { get; set; } = "Blue";
}
