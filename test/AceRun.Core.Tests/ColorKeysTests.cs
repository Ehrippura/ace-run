using System.Linq;
using ace_run.Models;
using ace_run.Services;
using Xunit;

namespace ace_run.Tests;

public class ColorKeysTests
{
    [Fact]
    public void The_keys_are_exactly_these_strings_in_this_order()
    {
        // Not a tautology, and not a snapshot for its own sake: these strings are written into
        // config.json and every workspace file as a workspace's ColorTag and a tag's ColorKey.
        // Renaming one silently drops the colour from every existing item that used it, and
        // the dialogs show display text from elsewhere, so nothing on screen would object.
        // The order is the palette order the pickers present.
        Assert.Equal(
            ["Blue", "Green", "Red", "Yellow", "Purple", "Gray"],
            ColorKeys.All);
    }

    [Fact]
    public void The_default_is_one_of_the_keys()
        => Assert.Contains(ColorKeys.Default, ColorKeys.All);

    [Fact]
    public void The_keys_are_distinct()
        => Assert.Equal(ColorKeys.All.Count, ColorKeys.All.Distinct().Count());

    [Fact]
    public void A_colour_key_round_trips_through_a_tag()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(
            new TagItem { Name = "Work", ColorKey = ColorKeys.Default }, AceRunJson.Options);

        Assert.Contains(ColorKeys.Default, json);

        var loaded = System.Text.Json.JsonSerializer.Deserialize<TagItem>(json, AceRunJson.Options);
        Assert.Equal(ColorKeys.Default, loaded!.ColorKey);
    }
}

public class WorkspaceImportTests
{
    [Fact]
    public void A_real_export_round_trips_back_into_an_accepted_import()
    {
        var json = DataStore.SerializeExport(new WorkspaceExport
        {
            Name = "Round trip",
            AppData = new AppData { UngroupedItems = { new AppItem { DisplayName = "A" } } }
        });

        Assert.Equal(ImportRejection.None, WorkspaceImport.TryParse(json, out var export));
        Assert.NotNull(export);
        Assert.Equal("Round trip", export.Name);
        Assert.Equal(1, export.AppData.ItemCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{ unterminated")]
    // Valid JSON, but not an object.
    [InlineData("[1, 2, 3]")]
    [InlineData("\"a string\"")]
    public void Anything_that_is_not_an_acerun_document_is_rejected(string? json)
    {
        Assert.Equal(ImportRejection.NotAnAceRunFile, WorkspaceImport.TryParse(json, out var export));
        Assert.Null(export);
    }

    [Fact]
    public void A_json_object_with_no_AppData_key_is_rejected()
    {
        // The case the old guard could not catch. WorkspaceExport.AppData carries a property
        // initializer, so System.Text.Json leaves a perfectly good empty instance in place for
        // a file that never mentioned it — an `AppData is null` test almost never fired, and
        // any stray .json renamed to .acerun imported as a blank workspace.
        Assert.Equal(ImportRejection.NotAnAceRunFile,
                     WorkspaceImport.TryParse("""{ "Name": "x" }""", out _));
    }

    [Fact]
    public void An_unrelated_json_object_is_rejected()
        => Assert.Equal(ImportRejection.NotAnAceRunFile,
                        WorkspaceImport.TryParse("""{ "foo": 1, "bar": [2] }""", out _));

    [Fact]
    public void An_explicitly_null_AppData_is_rejected()
        => Assert.Equal(ImportRejection.NotAnAceRunFile,
                        WorkspaceImport.TryParse("""{ "Name": "x", "AppData": null }""", out _));

    [Fact]
    public void A_file_from_a_newer_build_is_rejected()
    {
        // System.Text.Json drops keys it has no property for, so this used to import silently
        // — losing whatever the newer build had added and saying nothing about it.
        var json = $$"""
            { "AceRunVersion": {{WorkspaceExport.CurrentVersion + 1}}, "Name": "Future", "AppData": {} }
            """;

        Assert.Equal(ImportRejection.NewerVersion, WorkspaceImport.TryParse(json, out var export));
        Assert.Null(export);
    }

    [Fact]
    public void A_file_from_an_older_build_is_still_accepted()
    {
        Assert.Equal(ImportRejection.None,
                     WorkspaceImport.TryParse("""{ "AceRunVersion": 0, "Name": "Old", "AppData": {} }""", out _));
    }

    [Fact]
    public void A_blank_name_is_not_a_rejection()
    {
        // The caller substitutes its own localized default, the same one a workspace created
        // by hand gets. Refusing the file over a name would be a worse trade.
        Assert.Equal(ImportRejection.None,
                     WorkspaceImport.TryParse("""{ "Name": "", "AppData": {} }""", out var export));
        Assert.Equal(string.Empty, export!.Name);
    }

    [Fact]
    public void An_export_with_folders_keeps_them()
    {
        var json = DataStore.SerializeExport(new WorkspaceExport
        {
            Name = "Grouped",
            AppData = new AppData
            {
                Folders =
                {
                    new FolderItem { DisplayName = "Games", Children = { new AppItem { DisplayName = "A" } } }
                }
            }
        });

        Assert.Equal(ImportRejection.None, WorkspaceImport.TryParse(json, out var export));
        Assert.Equal("Games", Assert.Single(export!.AppData.Folders).DisplayName);
    }
}
