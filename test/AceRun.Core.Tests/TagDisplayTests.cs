using System;
using System.Collections.Generic;
using System.Linq;
using ace_run.Services;
using Xunit;

namespace ace_run.Tests;

public class TagDisplayTests
{
    private static List<string> Tags(int count)
        => Enumerable.Range(1, count).Select(i => $"Tag {i}").ToList();

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Everything_shows_while_it_fits(int count)
    {
        var tags = Tags(count);

        Assert.Equal(count, TagDisplay.Visible(tags).Count);
        Assert.Equal(0, TagDisplay.OverflowCount(tags));
    }

    [Theory]
    [InlineData(4, 1)]
    [InlineData(5, 2)]
    [InlineData(10, 7)]
    public void Beyond_the_limit_the_rest_are_counted(int count, int expectedOverflow)
    {
        var tags = Tags(count);

        Assert.Equal(TagDisplay.MaxVisible, TagDisplay.Visible(tags).Count);
        Assert.Equal(expectedOverflow, TagDisplay.OverflowCount(tags));
    }

    [Fact]
    public void The_leading_tags_are_the_ones_kept()
    {
        // Order is the workspace tag order, which TagOrdering maintains — so truncating takes
        // the tags the user ranked highest, not an arbitrary subset.
        Assert.Equal(new[] { "Tag 1", "Tag 2", "Tag 3" }, TagDisplay.Visible(Tags(5)));
    }

    [Fact]
    public void An_untruncated_list_is_handed_back_as_is()
    {
        // Not a micro-optimization for its own sake: this is read once per bound item, so the
        // common case must not allocate a copy on every read while the grid scrolls.
        var tags = Tags(3);

        Assert.Same(tags, TagDisplay.Visible(tags));
    }

    [Fact]
    public void A_truncated_list_is_a_copy_and_leaves_the_original_alone()
    {
        var tags = Tags(5);

        var visible = TagDisplay.Visible(tags);

        Assert.NotSame(tags, visible);
        Assert.Equal(5, tags.Count);
    }

    [Fact]
    public void The_two_halves_always_add_up()
    {
        // Visible + overflow must equal the whole, or the counter lies about how many are
        // hidden — the one way these two could drift apart.
        for (var count = 0; count <= 10; count++)
        {
            var tags = Tags(count);

            Assert.Equal(count, TagDisplay.Visible(tags).Count + TagDisplay.OverflowCount(tags));
        }
    }

    [Fact]
    public void An_empty_list_shows_and_counts_nothing()
    {
        var tags = new List<string>();

        Assert.Empty(TagDisplay.Visible(tags));
        Assert.Equal(0, TagDisplay.OverflowCount(tags));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public void A_caller_can_ask_for_a_different_limit(int max)
    {
        var tags = Tags(6);

        Assert.Equal(max, TagDisplay.Visible(tags, max).Count);
        Assert.Equal(6 - max, TagDisplay.OverflowCount(tags, max));
    }
}
