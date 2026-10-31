using System;
using System.IO;
using System.Text;
using ace_run.Automation;
using ace_run.Models;
using ace_run.Services;
using Xunit;

namespace ace_run.Tests;

/// <summary>
/// The reader the automation server uses. Its whole reason to exist is that it answers
/// "I could not read it" where <see cref="DataStore"/> answers "there is nothing here", so
/// most of this file is about failure reporting rather than about parsing.
/// </summary>
public sealed class AutomationReadTests : IDisposable
{
    private readonly string _root;
    private readonly AceRunPaths _paths;
    private readonly DataStore _store;

    // No delay: the retry loop is exercised for its decision, not for its patience.
    private readonly AutomationReader _reader;

    public AutomationReadTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "AceRunTests", Guid.NewGuid().ToString("N"));
        _paths = new AceRunPaths(_root);
        _store = new DataStore(_paths);
        _reader = new AutomationReader(_paths, attempts: 2, retryDelayMs: 0);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* a leftover temp dir is not worth failing a test over */ }
    }

    // --- Happy path ---

    [Fact]
    public void Reads_a_config_the_store_wrote()
    {
        var config = new WorkspaceConfig();
        var ws = new WorkspaceInfo { Name = "Games" };
        config.Workspaces.Add(ws);
        config.ActiveWorkspaceId = ws.Id;
        _store.SaveConfig(config);

        var result = _reader.ReadConfig();

        Assert.True(result.Ok);
        Assert.Equal(ReadOutcome.Ok, result.Outcome);
        Assert.Equal("Games", Assert.Single(result.Value!.Workspaces).Name);
    }

    [Fact]
    public void Reads_a_workspace_the_store_wrote()
    {
        var id = Guid.NewGuid();
        var data = new AppData();
        data.UngroupedItems.Add(new AppItem { DisplayName = "Notepad" });
        _store.SaveWorkspace(id, data);

        var result = _reader.ReadWorkspace(id);

        Assert.True(result.Ok);
        Assert.Equal("Notepad", Assert.Single(result.Value!.UngroupedItems).DisplayName);
    }

    // --- Failure reporting ---

    [Fact]
    public void A_missing_file_is_Missing_not_an_empty_workspace()
    {
        var result = _reader.ReadWorkspace(Guid.NewGuid());

        Assert.Equal(ReadOutcome.Missing, result.Outcome);
        Assert.False(result.Ok);
        Assert.Null(result.Value);
    }

    [Fact]
    public void Malformed_json_is_Malformed()
    {
        Write(_paths.ConfigFile, "{ this is not json");

        Assert.Equal(ReadOutcome.Malformed, _reader.ReadConfig().Outcome);
    }

    [Fact]
    public void An_empty_file_is_Malformed()
    {
        Write(_paths.ConfigFile, string.Empty);

        Assert.Equal(ReadOutcome.Malformed, _reader.ReadConfig().Outcome);
    }

    [Fact]
    public void A_file_holding_the_json_literal_null_is_Malformed()
    {
        // Deserialize succeeds here and hands back null, which is not a config to answer with.
        Write(_paths.ConfigFile, "null");

        Assert.Equal(ReadOutcome.Malformed, _reader.ReadConfig().Outcome);
    }

    [Fact]
    public void A_locked_file_is_Unreadable_after_the_last_try()
    {
        Write(_paths.ConfigFile, "{}");

        using var exclusive = new FileStream(
            _paths.ConfigFile, FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.Equal(ReadOutcome.Unreadable, _reader.ReadConfig().Outcome);
    }

    [Fact]
    public void The_same_malformed_file_reads_as_empty_through_DataStore()
    {
        // The contrast this reader exists for. DataStore is right to be forgiving — the app
        // has to start — but the automation server reporting an unreadable file as "you have
        // no workspaces" would be a confident wrong answer.
        Write(_paths.ConfigFile, "{ this is not json");

        Assert.Empty(_store.LoadConfig().Workspaces);
        Assert.Equal(ReadOutcome.Malformed, _reader.ReadConfig().Outcome);
    }

    // --- Share mode ---

    [Fact]
    public void A_shared_read_leaves_the_file_writable()
    {
        // The app writes its data files while this reader may be in the middle of one. Holding
        // them in a mode that denied that would let a query interfere with a save.
        Write(_paths.ConfigFile, "{}");

        using var held = AutomationReader.OpenShared(_paths.ConfigFile);

        using var writer = new FileStream(
            _paths.ConfigFile, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);

        writer.WriteByte((byte)' ');
    }

    [Fact]
    public void A_plain_read_handle_would_lock_the_writer_out()
    {
        // The inverse, so the test above is known to have teeth: File.ReadAllText opens with
        // FileShare.Read, which denies write access for as long as the handle lives.
        Write(_paths.ConfigFile, "{}");

        using var held = new FileStream(
            _paths.ConfigFile, FileMode.Open, FileAccess.Read, FileShare.Read);

        Assert.ThrowsAny<IOException>(() => new FileStream(
            _paths.ConfigFile, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete));
    }

    [Fact]
    public void No_share_mode_lets_a_rename_over_an_open_file_succeed()
    {
        // Pinned because the comment in AutomationReader used to claim the opposite: on
        // Windows, MoveFileEx with REPLACE_EXISTING fails while any handle to the destination
        // is open, FILE_SHARE_DELETE included — a pending deletion still owns the name the
        // rename needs. DataStore.WriteAtomic ends in exactly that call, so the only thing
        // this side can do about it is keep the handle open for as short a time as possible.
        Write(_paths.ConfigFile, "{}");
        var replacement = _paths.ConfigFile + ".tmp";
        Write(replacement, "{\"Version\":2}");

        using (var held = AutomationReader.OpenShared(_paths.ConfigFile))
        {
            Assert.ThrowsAny<Exception>(
                () => File.Move(replacement, _paths.ConfigFile, overwrite: true));
        }

        // ... and it goes through the moment the handle is gone.
        File.Move(replacement, _paths.ConfigFile, overwrite: true);
        Assert.Contains("Version", File.ReadAllText(_paths.ConfigFile));
    }

    // --- Construction ---

    [Fact]
    public void Constructing_a_reader_touches_nothing_on_disk()
    {
        _ = new AutomationReader(new AceRunPaths(Path.Combine(_root, "elsewhere")));

        Assert.False(Directory.Exists(Path.Combine(_root, "elsewhere")));
    }

    [Fact]
    public void Attempts_below_one_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AutomationReader(_paths, attempts: 0));
    }

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }
}
