using System;
using System.Collections.Generic;
using System.Linq;
using ace_run.Automation;
using ace_run.Models;
using Xunit;

namespace ace_run.Tests;

/// <summary>
/// The pipe contract: what a request has to carry, how it survives the trip, and the properties
/// the two ends rely on without being able to see each other's code.
/// </summary>
public sealed class AutomationCommandTests
{
    // --- Framing ---

    [Fact]
    public void A_request_fits_on_one_line_however_many_newlines_its_values_contain()
    {
        // NDJSON is the framing, and the reader on each end is a ReadLine. A value with a
        // newline in it — a display name pasted out of somewhere — must not become two
        // messages, neither of which parses.
        var request = new AutomationRequest
        {
            Id = "1",
            Command = nameof(AutomationAction.AddItem),
            Name = "two\nlines\r\nand a tab\t",
            Target = @"C:\Program Files\App\app.exe"
        };

        var line = AutomationPipe.Serialize(request);

        Assert.DoesNotContain('\n', line);
        Assert.DoesNotContain('\r', line);
    }

    [Fact]
    public void A_response_fits_on_one_line_too()
    {
        var response = AutomationResponse.Failed(
            "1", AutomationError.LaunchFailed, detail: "it broke\nbadly");

        Assert.DoesNotContain('\n', AutomationPipe.Serialize(response));
    }

    [Fact]
    public void Non_ascii_text_travels_as_itself_rather_than_as_escapes()
    {
        // Same reason the tool results do it: a library of Japanese titles doubles in size when
        // every character becomes \uXXXX, and these lines carry a whole workspace.
        var line = AutomationPipe.Serialize(new AutomationRequest { Name = "ゆずソフト" });

        Assert.Contains("ゆずソフト", line);
    }

    [Fact]
    public void A_request_round_trips_through_the_pipe_encoding()
    {
        var sent = new AutomationRequest
        {
            Id = "abc",
            Command = nameof(AutomationAction.UpdateItem),
            Item = "Notepad",
            Arguments = string.Empty,
            RunAsAdmin = true,
            Tags = new[] { "Done", "Progress" }
        };

        var received = AutomationPipe.TryDeserialize<AutomationRequest>(AutomationPipe.Serialize(sent));

        Assert.NotNull(received);
        Assert.Equal(sent.Tags, received!.Tags);
        // The record compares Tags by reference, and a list is not the array it was sent as, so
        // the rest is compared with that field taken out of both sides.
        Assert.Equal(sent with { Tags = null }, received with { Tags = null });

        // Empty and absent are different answers on an update, so the round trip has to keep
        // them apart: one clears the field, the other leaves it alone.
        Assert.Equal(string.Empty, received.Arguments);
        Assert.Null(received.WorkingDirectory);
    }

    [Fact]
    public void A_response_carrying_a_state_round_trips_with_its_workspace_contents()
    {
        var folder = new FolderItem { DisplayName = "Tools" };
        folder.Children.Add(new AppItem { DisplayName = "Hex Editor", FilePath = @"C:\hex.exe" });

        var data = new AppData();
        data.Folders.Add(folder);
        data.Tags.Add(new TagItem { Name = "Done", ColorKey = "Green" });

        var sent = new AutomationResponse
        {
            Id = "1",
            State = new AutomationState
            {
                Config = new WorkspaceConfig { Workspaces = { new WorkspaceInfo { Name = "Work" } } },
                ActiveWorkspaceId = Guid.NewGuid(),
                ActiveData = data,
                Mode = AutomationMode.Full
            }
        };

        var received = AutomationPipe.TryDeserialize<AutomationResponse>(AutomationPipe.Serialize(sent));

        Assert.NotNull(received?.State);
        Assert.Equal(AutomationMode.Full, received!.State!.Mode);
        Assert.Equal(sent.State!.ActiveWorkspaceId, received.State.ActiveWorkspaceId);
        Assert.Equal("Hex Editor", received.State.ActiveData.Folders[0].Children[0].DisplayName);
        Assert.Equal("Done", received.State.ActiveData.Tags[0].Name);
        Assert.Equal("Work", received.State.Config.Workspaces[0].Name);
    }

    [Fact]
    public void A_line_that_is_not_valid_json_comes_back_as_null_rather_than_throwing()
    {
        // The server has to answer every line it reads, including one it cannot parse, so the
        // parse failure must be a value rather than an exception on the read loop.
        Assert.Null(AutomationPipe.TryDeserialize<AutomationRequest>("{ not json"));
    }

    [Fact]
    public void The_pipe_name_carries_the_users_sid_and_is_stable_within_a_process()
    {
        Assert.StartsWith("AceRun.Automation.", AutomationPipe.Name);
        Assert.Same(AutomationPipe.Name, AutomationPipe.Name);
    }

    // --- Action parsing ---

    [Fact]
    public void An_action_name_this_build_does_not_know_parses_as_Unknown_rather_than_throwing()
    {
        // The whole reason Command is a string on the wire: an enum member from a newer server
        // would make the deserializer throw on the line, leaving no id to answer with.
        var line = """{"id":"1","command":"RewriteEverything"}""";

        var request = AutomationPipe.TryDeserialize<AutomationRequest>(line);

        Assert.NotNull(request);
        Assert.Equal("1", request!.Id);
        Assert.Equal(AutomationAction.Unknown, request.Action);
        Assert.Equal(AutomationError.UnknownCommand, request.Validate());
    }

    [Fact]
    public void An_action_name_is_matched_without_case()
    {
        Assert.Equal(
            AutomationAction.LaunchItem,
            new AutomationRequest { Command = "launchitem" }.Action);
    }

    [Fact]
    public void A_numeric_command_is_not_an_action()
    {
        // Enum.TryParse accepts the underlying numbers, which would let "2" mean whichever
        // action happens to be second today.
        Assert.Equal(AutomationAction.Unknown, new AutomationRequest { Command = "2" }.Action);
        Assert.Equal(AutomationAction.Unknown, new AutomationRequest { Command = "" }.Action);
    }

    // --- Completeness ---

    [Fact]
    public void Every_action_is_accounted_for_by_Validate()
    {
        // Internal is the switch's default arm: reaching it means an action was added without
        // deciding what it requires, which on the app side would be a request accepted with no
        // arguments checked at all.
        foreach (var action in Enum.GetValues<AutomationAction>())
        {
            var outcome = Full(action).Validate();

            Assert.NotEqual(AutomationError.Internal, outcome);

            if (action != AutomationAction.Unknown)
                Assert.Equal(AutomationError.None, outcome);
        }
    }

    [Fact]
    public void ReadState_is_the_only_action_that_changes_nothing()
    {
        // What the read-only setting is enforced on. Showing or hiding a window counts as
        // changing something: read-only means look, do not touch.
        foreach (var action in Enum.GetValues<AutomationAction>())
            Assert.Equal(
                action != AutomationAction.ReadState,
                new AutomationRequest { Command = action.ToString() }.ChangesSomething);
    }

    // --- Required arguments ---

    [Theory]
    [InlineData(AutomationAction.LaunchItem)]
    [InlineData(AutomationAction.DeleteItem)]
    [InlineData(AutomationAction.MoveItem)]
    [InlineData(AutomationAction.SetItemTags)]
    public void An_action_on_an_item_needs_one_named(AutomationAction action)
    {
        var request = Full(action) with { Item = "   " };

        Assert.Equal(AutomationError.MissingArgument, request.Validate());
    }

    [Fact]
    public void AddItem_needs_both_a_target_and_a_kind()
    {
        var full = Full(AutomationAction.AddItem);

        Assert.Equal(AutomationError.MissingArgument, (full with { Target = null }).Validate());
        Assert.Equal(AutomationError.MissingArgument, (full with { Kind = null }).Validate());
    }

    [Fact]
    public void UpdateItem_with_nothing_to_change_is_a_missing_argument()
    {
        // Not a no-op to confirm: the caller meant to set something and left it out, and
        // answering "done" would have it believe the change landed.
        var request = new AutomationRequest
        {
            Command = nameof(AutomationAction.UpdateItem),
            Item = "Notepad"
        };

        Assert.Equal(AutomationError.MissingArgument, request.Validate());
    }

    [Theory]
    [InlineData("")]
    [InlineData("new name")]
    public void UpdateItem_with_any_one_field_is_enough(string name)
    {
        var request = new AutomationRequest
        {
            Command = nameof(AutomationAction.UpdateItem),
            Item = "Notepad",
            Name = name
        };

        Assert.Equal(AutomationError.None, request.Validate());
    }

    [Fact]
    public void SetItemTags_tells_an_empty_list_from_a_missing_one()
    {
        var request = new AutomationRequest
        {
            Command = nameof(AutomationAction.SetItemTags),
            Item = "Notepad"
        };

        // No list at all is a mistake; an empty list is an instruction to clear the tags.
        Assert.Equal(AutomationError.MissingArgument, request.Validate());
        Assert.Equal(
            AutomationError.None,
            (request with { Tags = Array.Empty<string>() }).Validate());
    }

    [Fact]
    public void UpdateTag_needs_the_tag_and_something_to_change()
    {
        var full = Full(AutomationAction.UpdateTag);

        Assert.Equal(AutomationError.MissingArgument, (full with { Tag = "   " }).Validate());
        Assert.Equal(
            AutomationError.MissingArgument,
            (full with { Name = null, Color = null }).Validate());
        Assert.Equal(AutomationError.None, (full with { Name = null, Color = "Red" }).Validate());
    }

    [Fact]
    public void DeleteTag_needs_the_tag_and_nothing_else()
    {
        var full = Full(AutomationAction.DeleteTag);

        Assert.Equal(AutomationError.MissingArgument, (full with { Tag = null }).Validate());
        Assert.Equal(AutomationError.None, (full with { Name = null, Color = null }).Validate());
    }

    [Fact]
    public void Folder_commands_need_the_folder_they_act_on()
    {
        Assert.Equal(
            AutomationError.MissingArgument,
            (Full(AutomationAction.DeleteFolder) with { Folder = null }).Validate());

        Assert.Equal(
            AutomationError.MissingArgument,
            (Full(AutomationAction.RenameFolder) with { Name = null }).Validate());
    }

    [Fact]
    public void An_add_or_a_move_with_no_folder_is_valid_because_that_means_ungrouped()
    {
        Assert.Equal(
            AutomationError.None,
            (Full(AutomationAction.MoveItem) with { Folder = null }).Validate());

        Assert.Equal(
            AutomationError.None,
            (Full(AutomationAction.AddItem) with { Folder = null }).Validate());
    }

    [Fact]
    public void SwitchWorkspace_is_the_one_action_that_needs_a_workspace()
    {
        Assert.Equal(
            AutomationError.MissingArgument,
            (Full(AutomationAction.SwitchWorkspace) with { Workspace = null }).Validate());

        // Everywhere else it is an assertion the caller may leave out.
        Assert.Equal(
            AutomationError.None,
            (Full(AutomationAction.LaunchItem) with { Workspace = null }).Validate());
    }

    // --- Responses ---

    [Fact]
    public void A_failed_response_is_not_Ok_and_carries_its_candidates()
    {
        var candidates = new List<LookupCandidate> { new(Guid.NewGuid(), "Zero Escape") };

        var response = AutomationResponse.Failed("1", AutomationError.ItemAmbiguous, candidates);

        Assert.False(response.Ok);
        Assert.Equal("Zero Escape", Assert.Single(response.Candidates!).Name);
        Assert.True(AutomationResponse.Done("1").Ok);
    }

    [Fact]
    public void Ok_is_not_written_to_the_wire()
    {
        // A get-only property would otherwise be serialized on every single line.
        Assert.DoesNotContain("\"ok\"", AutomationPipe.Serialize(AutomationResponse.Done("1")));
    }

    /// <summary>A request with every argument any action could want, so only the action varies.</summary>
    private static AutomationRequest Full(AutomationAction action) => new()
    {
        Id = "1",
        Command = action.ToString(),
        Item = "Notepad",
        Folder = "Tools",
        Name = "Notepad",
        Target = @"C:\notepad.exe",
        Kind = nameof(ItemKind.App),
        Workspace = "Work",
        Tag = "Done",
        Tags = new[] { "Done" }
    };
}
