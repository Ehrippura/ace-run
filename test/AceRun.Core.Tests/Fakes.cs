using System;
using System.Collections.Generic;
using System.Linq;
using ace_run.Models;

namespace ace_run.Tests;

/// <summary>
/// Stand-ins for the app's view models. The interfaces exist precisely so the logic layer can
/// be exercised without WinUI, and these are what proves it: nothing here references the app.
/// </summary>
internal sealed class FakeTag : ITagRef
{
    public FakeTag(string name, Guid? id = null)
    {
        Id = id ?? Guid.NewGuid();
        Name = name;
    }

    public Guid Id { get; }
    public string Name { get; }

    public override string ToString() => Name;
}

internal sealed class FakeItem : IAppItemView
{
    public FakeItem(
        string displayName,
        string filePath = "",
        string arguments = "",
        string sortKey = "",
        params ITagRef[] tags)
    {
        DisplayName = displayName;
        FilePath = filePath;
        Arguments = arguments;
        SortKey = sortKey;
        TagList = tags.ToList();
    }

    public Guid Id { get; init; } = Guid.NewGuid();
    public string DisplayName { get; }
    public string FilePath { get; }
    public string Arguments { get; }
    public string SortKey { get; }

    public List<ITagRef> TagList { get; }
    public IEnumerable<ITagRef> Tags => TagList;

    public override string ToString() => DisplayName;
}
