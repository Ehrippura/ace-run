using System;
using System.Collections.Generic;

namespace ace_run.Models;

/// <summary>
/// The parts of a tag that logic cares about: which tag it is, and what it is called.
/// </summary>
/// <remarks>
/// Implemented by the app's <c>TagViewModel</c>, which also carries a WinUI <c>Brush</c> and
/// so cannot itself live here.
/// </remarks>
public interface ITagRef
{
    Guid Id { get; }
    string Name { get; }
}

/// <summary>
/// The parts of a launch item that logic cares about — searching, ordering, recording a
/// launch. Deliberately read-only: nothing in this layer mutates an item.
/// </summary>
/// <remarks>
/// This interface exists to keep WinUI out of the logic layer. Search and Organize both run
/// over the live view models rather than over <see cref="AppItem"/>, because collection order
/// <em>is</em> the persisted order and the collections hold view models — but
/// <c>AppItemViewModel</c> exposes <c>Visibility</c>, a <c>BitmapImage</c> and a tag list of
/// view models, so taking it directly would drag the whole framework across the boundary.
///
/// <c>Tags</c> is <see cref="IEnumerable{T}"/> rather than a list so the app's
/// <c>ObservableCollection&lt;TagViewModel&gt;</c> satisfies it by covariance, with no
/// projection and no allocation per call.
/// </remarks>
public interface IAppItemView
{
    Guid Id { get; }
    string DisplayName { get; }

    /// <summary>An .exe path or a URL, depending on the item's kind. Compared, never launched, here.</summary>
    string FilePath { get; }

    string Arguments { get; }

    /// <summary>User-defined ordering key. Empty means "unspecified", which sorts last.</summary>
    string SortKey { get; }

    /// <summary>In workspace tag order — the invariant <c>NormalizeAppTags</c> maintains.</summary>
    IEnumerable<ITagRef> Tags { get; }
}
