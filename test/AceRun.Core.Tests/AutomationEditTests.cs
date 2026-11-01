using System;
using System.IO;
using System.Text.Json;
using ace_run.Automation;
using ace_run.Models;
using ace_run.Services;
using Xunit;

namespace ace_run.Tests;

/// <summary>
/// What an automation write is allowed to create and change.
/// </summary>
/// <remarks>
/// The checking is in Core so it can be tested without a window, and these tests are why it is
/// worth having: the caller is an agent working from a path it may have inferred, and an item
/// that points at nothing is one the user has to find and delete later, having never asked for
/// it.
/// </remarks>
public sealed class AutomationEditTests : IDisposable
{
    private readonly string _dir;
    private readonly string _exe;

    public AutomationEditTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "AceRunEdit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _exe = Path.Combine(_dir, "app.exe");
        File.WriteAllText(_exe, "not really an executable");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
    }

    // --- Adding an app ---

    [Fact]
    public void A_new_app_is_built_the_way_the_picker_builds_one()
    {
        var error = AutomationEdit.NewItem(Add(nameof(ItemKind.App), _exe), out var item);

        Assert.Equal(AutomationError.None, error);
        Assert.Equal(ItemKind.App, item.Kind);
        Assert.Equal(_exe, item.FilePath);
        // Through ItemFactory, which is what gives it the name and the working directory a
        // dropped or picked file would have had.
        Assert.Equal("app", item.DisplayName);
        Assert.Equal(_dir, item.WorkingDirectory);
    }

    [Fact]
    public void A_path_with_no_file_at_it_is_refused()
    {
        var error = AutomationEdit.NewItem(
            Add(nameof(ItemKind.App), Path.Combine(_dir, "missing.exe")), out _);

        Assert.Equal(AutomationError.InvalidTarget, error);
    }

    [Fact]
    public void A_path_wearing_quotes_is_accepted_with_them_stripped()
    {
        // Quotes are not valid in a Windows filename, so nothing legitimate is lost — and a
        // path copied out of a command line arrives wearing them.
        var error = AutomationEdit.NewItem(Add(nameof(ItemKind.App), $"\"{_exe}\""), out var item);

        Assert.Equal(AutomationError.None, error);
        Assert.Equal(_exe, item.FilePath);
    }

    [Fact]
    public void A_given_name_wins_over_the_derived_one_and_is_trimmed()
    {
        var error = AutomationEdit.NewItem(
            Add(nameof(ItemKind.App), _exe) with { Name = "  My App  " }, out var item);

        Assert.Equal(AutomationError.None, error);
        Assert.Equal("My App", item.DisplayName);
    }

    [Fact]
    public void A_blank_name_is_refused_rather_than_falling_back_to_the_derived_one()
    {
        // The caller said what it wanted the name to be. Quietly substituting another is how an
        // agent ends up reporting an item it did not create.
        var error = AutomationEdit.NewItem(
            Add(nameof(ItemKind.App), _exe) with { Name = "   " }, out _);

        Assert.Equal(AutomationError.InvalidName, error);
    }

    [Fact]
    public void Arguments_and_an_explicit_working_directory_are_kept()
    {
        var request = Add(nameof(ItemKind.App), _exe) with
        {
            Arguments = "--fullscreen",
            WorkingDirectory = _dir,
            RunAsAdmin = true
        };

        Assert.Equal(AutomationError.None, AutomationEdit.NewItem(request, out var item));
        Assert.Equal("--fullscreen", item.Arguments);
        Assert.Equal(_dir, item.WorkingDirectory);
        Assert.True(item.RunAsAdmin);
    }

    [Fact]
    public void A_working_directory_that_does_not_exist_is_refused()
    {
        // ProcessStartInfo throws on one at launch time, which would make it the user's problem
        // long after the agent has reported success.
        var request = Add(nameof(ItemKind.App), _exe) with
        {
            WorkingDirectory = Path.Combine(_dir, "nowhere")
        };

        Assert.Equal(AutomationError.InvalidTarget, AutomationEdit.NewItem(request, out _));
    }

    // --- Adding a URL ---

    [Fact]
    public void A_bare_host_becomes_an_https_url_named_after_itself()
    {
        var error = AutomationEdit.NewItem(Add(nameof(ItemKind.Url), "github.com"), out var item);

        Assert.Equal(AutomationError.None, error);
        Assert.Equal(ItemKind.Url, item.Kind);
        Assert.Equal("https://github.com", item.FilePath);
        Assert.Equal("github.com", item.DisplayName);
    }

    [Fact]
    public void A_custom_protocol_is_a_url_and_keeps_its_whole_text_as_a_name()
    {
        var error = AutomationEdit.NewItem(Add(nameof(ItemKind.Url), "ms-settings:display"), out var item);

        Assert.Equal(AutomationError.None, error);
        Assert.Equal("ms-settings:display", item.FilePath);
        // No host to name it after, so SuggestDisplayName returns the whole thing.
        Assert.Equal("ms-settings:display", item.DisplayName);
    }

    [Fact]
    public void A_local_path_is_not_a_url()
    {
        Assert.Equal(
            AutomationError.InvalidTarget,
            AutomationEdit.NewItem(Add(nameof(ItemKind.Url), _exe), out _));
    }

    [Fact]
    public void A_url_item_refuses_the_three_fields_it_would_ignore()
    {
        // 7-url-items.md §5: a URL launches through the shell, which takes none of these.
        // Storing them would describe a launch that cannot happen.
        var url = Add(nameof(ItemKind.Url), "https://example.com");

        Assert.Equal(
            AutomationError.FieldNotApplicable,
            AutomationEdit.NewItem(url with { Arguments = "-x" }, out _));
        Assert.Equal(
            AutomationError.FieldNotApplicable,
            AutomationEdit.NewItem(url with { WorkingDirectory = _dir }, out _));
        Assert.Equal(
            AutomationError.FieldNotApplicable,
            AutomationEdit.NewItem(url with { RunAsAdmin = true }, out _));
    }

    // --- Kinds ---

    [Theory]
    [InlineData("exe")]
    [InlineData("")]
    [InlineData(null)]
    public void A_kind_that_is_not_App_or_Url_is_refused(string? kind)
    {
        Assert.Equal(
            AutomationError.InvalidKind,
            AutomationEdit.NewItem(Add(kind, _exe), out _));
    }

    [Fact]
    public void A_kind_is_matched_without_case()
    {
        Assert.Equal(AutomationError.None, AutomationEdit.NewItem(Add("url", "example.com"), out var item));
        Assert.Equal(ItemKind.Url, item.Kind);
    }

    // --- Updating ---

    [Fact]
    public void An_update_leaves_out_what_it_was_not_asked_to_change()
    {
        var request = new AutomationRequest
        {
            Command = nameof(AutomationAction.UpdateItem),
            Item = "app",
            Arguments = string.Empty
        };

        Assert.Equal(AutomationError.None, AutomationEdit.Edits(request, ItemKind.App, out var edits));

        // Empty clears, null leaves alone — the distinction the whole flat-request shape rests on.
        Assert.Equal(string.Empty, edits.Arguments);
        Assert.Null(edits.Name);
        Assert.Null(edits.Target);
        Assert.Null(edits.WorkingDirectory);
        Assert.Null(edits.RunAsAdmin);
    }

    [Fact]
    public void An_update_can_clear_the_working_directory()
    {
        var request = new AutomationRequest
        {
            Command = nameof(AutomationAction.UpdateItem),
            Item = "app",
            WorkingDirectory = string.Empty
        };

        Assert.Equal(AutomationError.None, AutomationEdit.Edits(request, ItemKind.App, out var edits));
        Assert.Equal(string.Empty, edits.WorkingDirectory);
    }

    [Fact]
    public void An_update_cannot_turn_a_file_item_into_a_url_item()
    {
        // 7-url-items.md §4: the kind is fixed at creation, and every kind-specific path
        // downstream assumes it cannot change under them.
        var request = new AutomationRequest
        {
            Command = nameof(AutomationAction.UpdateItem),
            Item = "app",
            Kind = nameof(ItemKind.Url)
        };

        Assert.Equal(
            AutomationError.KindNotEditable,
            AutomationEdit.Edits(request, ItemKind.App, out _));
    }

    [Fact]
    public void An_update_that_states_the_kind_it_already_is_is_allowed()
    {
        var request = new AutomationRequest
        {
            Command = nameof(AutomationAction.UpdateItem),
            Item = "app",
            Kind = "app",
            Name = "Renamed"
        };

        Assert.Equal(AutomationError.None, AutomationEdit.Edits(request, ItemKind.App, out var edits));
        Assert.Equal("Renamed", edits.Name);
    }

    [Fact]
    public void An_update_checks_a_new_target_the_same_way_an_add_does()
    {
        var request = new AutomationRequest
        {
            Command = nameof(AutomationAction.UpdateItem),
            Item = "app",
            Target = Path.Combine(_dir, "missing.exe")
        };

        Assert.Equal(AutomationError.InvalidTarget, AutomationEdit.Edits(request, ItemKind.App, out _));
    }

    [Fact]
    public void An_update_to_a_url_item_refuses_the_fields_a_url_ignores()
    {
        var request = new AutomationRequest
        {
            Command = nameof(AutomationAction.UpdateItem),
            Item = "site",
            RunAsAdmin = false
        };

        Assert.Equal(
            AutomationError.FieldNotApplicable,
            AutomationEdit.Edits(request, ItemKind.Url, out _));
    }

    // --- Names and colours ---

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_blank_folder_or_tag_name_is_refused(string? name)
    {
        Assert.Equal(AutomationError.InvalidName, AutomationEdit.ResolveName(name, out _));
    }

    [Fact]
    public void A_name_is_trimmed()
    {
        Assert.Equal(AutomationError.None, AutomationEdit.ResolveName("  Tools  ", out var name));
        Assert.Equal("Tools", name);
    }

    [Fact]
    public void An_omitted_colour_is_the_default_rather_than_an_error()
    {
        Assert.Equal(AutomationError.None, AutomationEdit.ResolveColor(null, out var key));
        Assert.Equal(ColorKeys.Default, key);
    }

    [Fact]
    public void A_colour_comes_back_in_the_casing_that_gets_persisted()
    {
        // The key is matched verbatim when the brush is resolved, and these strings are
        // persisted to JSON — "green" would store a colour nothing can look up.
        Assert.Equal(AutomationError.None, AutomationEdit.ResolveColor("green", out var key));
        Assert.Equal("Green", key);
        Assert.Contains(key, ColorKeys.All);
    }

    [Fact]
    public void A_colour_that_is_not_one_of_the_six_is_refused()
    {
        Assert.Equal(AutomationError.InvalidColor, AutomationEdit.ResolveColor("chartreuse", out _));
    }

    // --- Where the two executables live ---

    [Fact]
    public void A_sibling_is_found_only_when_it_is_actually_there()
    {
        Assert.Equal(_exe, AutomationLayout.Sibling(_dir, "app.exe"));
        Assert.Null(AutomationLayout.Sibling(_dir, AutomationLayout.McpExeName));
        Assert.Null(AutomationLayout.Sibling(null, "app.exe"));
        Assert.Null(AutomationLayout.Sibling(string.Empty, "app.exe"));
    }

    [Fact]
    public void The_client_configuration_names_the_server_with_its_backslashes_intact()
    {
        var json = AutomationLayout.ClientConfigJson(@"C:\Program Files\Ace Run\AceRun.Mcp.exe");

        // Round-tripped rather than string-matched: what matters is that a client parsing it
        // gets the path back, which is exactly what hand-written JSON gets wrong.
        using var document = JsonDocument.Parse(json);

        Assert.Equal(
            @"C:\Program Files\Ace Run\AceRun.Mcp.exe",
            document.RootElement
                .GetProperty("mcpServers")
                .GetProperty("ace-run")
                .GetProperty("command")
                .GetString());
    }

    private static AutomationRequest Add(string? kind, string target) => new()
    {
        Command = nameof(AutomationAction.AddItem),
        Kind = kind,
        Target = target
    };
}
