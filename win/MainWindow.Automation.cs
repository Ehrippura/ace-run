using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ace_run.Automation;
using ace_run.Models;
using ace_run.Services;
using Microsoft.UI.Xaml;

namespace ace_run;

/// <summary>
/// The whole of the automation adapter: pipe requests in, answers out, and every touch of the
/// live state in between.
/// </summary>
/// <remarks>
/// <para>
/// One partial file on purpose (12-mcp-automation.md §B2). Spreading these calls through the
/// existing partials would scatter the two rules that make the adapter safe, and both of them
/// have to hold everywhere: the work runs on the UI thread, and it never opens a dialog.
/// </para>
/// <para>
/// <b>No dialogs.</b> Nobody is looking at the screen, so a confirmation would wait forever, and
/// <c>_modalDepth</c> would fight it anyway. Writes are refused outright while the user has a
/// dialog or flyout open — see <see cref="AutomationError.Busy"/>.
/// </para>
/// <para>
/// <b>Saving goes through <see cref="CommitSave"/>.</b> Not <c>SaveItems()</c>, which returns
/// early while the search box has text: an automation write in that state would be accepted and
/// silently not written. <see cref="PersistAfterEdit"/> ends in the same place and is what the
/// shared cores call.
/// </para>
/// <para>
/// <b>Placement is explicit.</b> A command that adds or moves an item puts it where the request
/// said, not into whichever folder the user happens to have open. The open folder is a property
/// of the person at the keyboard and has nothing to do with a caller on a pipe.
/// </para>
/// </remarks>
public sealed partial class MainWindow
{
    private AutomationServer? _automationServer;

    /// <summary>
    /// Starts or stops the pipe to match <see cref="AppSettings.Automation"/>. Called from
    /// <see cref="ApplySettings"/> and again by the settings window on every change, so it has
    /// to be safe to call when nothing has changed.
    /// </summary>
    public void ApplyAutomationSetting()
    {
        if (Settings.Automation == AutomationMode.Off)
        {
            _automationServer?.Stop();
            return;
        }

        // Read-only and full both listen; the difference is enforced per request, so flipping
        // between them needs no restart and loses no connection.
        _automationServer ??= new AutomationServer(HandleAutomationAsync);
        _automationServer.Start();
    }

    private void StopAutomation() => _automationServer?.Stop();

    #region Thread hand-off

    /// <summary>
    /// Runs a request on the UI thread and hands the answer back to the pipe.
    /// </summary>
    /// <remarks>
    /// Called from a pipe reader on a thread pool thread. Everything below this line touches
    /// <see cref="System.Collections.ObjectModel.ObservableCollection{T}"/>s that WinUI is bound
    /// to, which may only be modified from the thread that owns them — hence the enqueue, and
    /// hence the <see cref="TaskCompletionSource{TResult}"/> to carry the result back out.
    /// </remarks>
    private Task<AutomationResponse> HandleAutomationAsync(AutomationRequest request)
    {
        var completion = new TaskCompletionSource<AutomationResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        if (!DispatcherQueue.TryEnqueue(() => _ = CompleteAsync(request, completion)))
            completion.SetResult(Failed(request, AutomationError.Internal,
                detail: "The window is not accepting work."));

        return completion.Task;
    }

    private async Task CompleteAsync(
        AutomationRequest request,
        TaskCompletionSource<AutomationResponse> completion)
    {
        try
        {
            completion.SetResult(await RunAsync(request));
        }
        catch (Exception ex)
        {
            // An exception here would otherwise be an unobserved failure on the UI thread with
            // a caller waiting on a reply that never comes.
            completion.SetResult(Failed(request, AutomationError.Internal, detail: ex.Message));
        }
    }

    #endregion

    #region Dispatch

    private async Task<AutomationResponse> RunAsync(AutomationRequest request)
    {
        // The gate before the parse: a request that should not be here at all gets the same
        // answer whether or not its arguments make sense.
        if (Settings.Automation == AutomationMode.Off)
            return Failed(request, AutomationError.Disabled);

        if (request.ChangesSomething && Settings.Automation != AutomationMode.Full)
            return Failed(request, AutomationError.ReadOnly);

        var invalid = request.Validate();
        if (invalid != AutomationError.None)
            return Failed(request, invalid);

        // A write while the user has a dialog up would move the collections that dialog is
        // editing. Reads are fine: they copy and change nothing.
        if (request.ChangesSomething && IsModal)
            return Failed(request, AutomationError.Busy);

        if (request.Action != AutomationAction.SwitchWorkspace
            && WorkspaceMismatch(request) is { } mismatch)
            return mismatch;

        return request.Action switch
        {
            AutomationAction.ReadState => ReadState(request),
            AutomationAction.LaunchItem => LaunchItem(request),
            AutomationAction.AddItem => AddItem(request),
            AutomationAction.UpdateItem => UpdateItem(request),
            AutomationAction.DeleteItem => DeleteItem(request),
            AutomationAction.MoveItem => MoveItem(request),
            AutomationAction.CreateFolder => CreateFolder(request),
            AutomationAction.RenameFolder => RenameFolder(request),
            AutomationAction.DeleteFolder => DeleteFolder(request),
            AutomationAction.SetItemTags => SetItemTags(request),
            AutomationAction.CreateTag => CreateTag(request),
            AutomationAction.SwitchWorkspace => await SwitchToWorkspaceAsync(request),
            AutomationAction.ShowWindow => ShowWindow(request),
            AutomationAction.HideWindow => HideWindow(request),
            AutomationAction.ClearRecentLaunches => ClearRecents(request),
            _ => Failed(request, AutomationError.UnknownCommand)
        };
    }

    /// <summary>
    /// Checks the workspace a request asserts it is addressing, if it named one.
    /// </summary>
    /// <remarks>
    /// An assertion rather than a selector: the window holds one workspace, and the alternative
    /// to refusing — switching to the named one and switching back — would move the user out of
    /// what they were looking at to serve a question they never asked. Being told to call
    /// <c>switch_workspace</c> costs the caller one extra round trip and costs the user nothing.
    /// </remarks>
    private AutomationResponse? WorkspaceMismatch(AutomationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Workspace)) return null;

        var found = ItemLookup.Workspace(_workspaceConfig, request.Workspace);

        if (found.Outcome == LookupOutcome.Ambiguous)
            return Failed(request, AutomationError.WorkspaceAmbiguous, found.Candidates);

        if (found.Value is null)
            return Failed(request, AutomationError.WorkspaceNotFound);

        return found.Value.Id == _currentWorkspace.Id
            ? null
            : Failed(request, AutomationError.WorkspaceNotActive, detail: _currentWorkspace.Name);
    }

    #endregion

    #region Reading

    private AutomationResponse ReadState(AutomationRequest request) =>
        new()
        {
            Id = request.Id,
            State = new AutomationState
            {
                // The workspace list and nothing else. Settings and the window state are left
                // at their defaults on purpose: the automation interface does not read
                // application settings (12-mcp-automation.md, out of scope), and sending the
                // hotkey and the rest down a wire with no use for them is not free.
                Config = new WorkspaceConfig
                {
                    Version = _workspaceConfig.Version,
                    Workspaces = _workspaceConfig.Workspaces,
                    ActiveWorkspaceId = _workspaceConfig.ActiveWorkspaceId
                },
                ActiveWorkspaceId = _currentWorkspace.Id,
                ActiveData = Snapshot(),
                Mode = Settings.Automation
            }
        };

    /// <summary>
    /// The live contents of the open workspace, as a value.
    /// </summary>
    /// <remarks>
    /// A copy, where <see cref="CommitSave"/> rebuilds <c>_appData</c> in place. It has to be:
    /// that instance is the one <see cref="TrackRecentLaunch"/> appends to and the one the save
    /// writes, and a read is not allowed to move anything. The three projection lines are the
    /// same three, deliberately — if a field is ever added to the workspace file, both places
    /// have to learn about it, and a reader comparing them will see that.
    /// </remarks>
    private AppData Snapshot() => new()
    {
        Version = _appData.Version,
        Tags = _tags.Select(t => t.ToModel()).ToList(),
        UngroupedItems = _ungroupedApps.Select(v => v.ToModel()).ToList(),
        Folders = _folders.Select(f => f.ToModel()).ToList(),
        RecentLaunches = new List<RecentLaunch>(_appData.RecentLaunches)
    };

    #endregion

    #region Items

    private AutomationResponse LaunchItem(AutomationRequest request)
    {
        if (Resolve(request, out var app) is { } failure) return failure;

        // Its own switch, deliberately separate from "automation may write": an elevated launch
        // puts a UAC prompt on the user's screen with nothing on it to say what asked for it.
        if (app.RunAsAdmin && !Settings.AutomationAllowAdminLaunch)
            return Failed(request, AutomationError.AdminLaunchNotAllowed);

        var described = Describe(app);
        var failed = LaunchCore(app);

        // The tail runs either way: the recent list and the tray are updated by AfterLaunch,
        // and LaunchCore only records a launch that started.
        AfterLaunch();

        return failed is null
            ? new AutomationResponse { Id = request.Id, Item = described }
            : Failed(request, AutomationError.LaunchFailed, detail: failed.Message);
    }

    private AutomationResponse AddItem(AutomationRequest request)
    {
        var invalid = AutomationEdit.NewItem(request, out var item);
        if (invalid != AutomationError.None) return Failed(request, invalid);

        if (TargetFolder(request, out var folder) is { } failure) return failure;

        var app = new AppItemViewModel(item, _tags);
        (folder?.Apps ?? _ungroupedApps).Add(app);

        // Fire and forget, as everywhere else: the icon arrives when the disk read finishes and
        // the answer does not wait for it.
        _ = app.LoadIconAsync();

        CommitSave();

        return new AutomationResponse { Id = request.Id, Item = Describe(app) };
    }

    private AutomationResponse UpdateItem(AutomationRequest request)
    {
        if (Resolve(request, out var app) is { } failure) return failure;

        var invalid = AutomationEdit.Edits(request, app.Kind, out var edits);
        if (invalid != AutomationError.None) return Failed(request, invalid);

        var previousPath = app.FilePath;

        if (edits.Name is not null) app.DisplayName = edits.Name;
        if (edits.Target is not null) app.FilePath = edits.Target;
        if (edits.Arguments is not null) app.Arguments = edits.Arguments;
        if (edits.WorkingDirectory is not null) app.WorkingDirectory = edits.WorkingDirectory;
        if (edits.RunAsAdmin is bool admin) app.RunAsAdmin = admin;

        // The cached icon was extracted from the old path and is keyed by the item id, so it
        // outlives the change. Invalidated here rather than from the property setter for the
        // reason the setter documents: a string assignment that deletes a file is a side effect
        // nobody reading the call site would expect. Same pass EditItemDialog.ApplyTo makes.
        if (app.FilePath != previousPath)
        {
            IconService.InvalidateCache(app.Id);
            _ = app.LoadIconAsync();
        }

        CommitSave();

        return new AutomationResponse { Id = request.Id, Item = Describe(app) };
    }

    private AutomationResponse DeleteItem(AutomationRequest request)
    {
        if (Resolve(request, out var app) is { } failure) return failure;

        // Described before it goes: afterwards there is no folder to name it by, and what the
        // caller needs to report is what was deleted.
        var described = Describe(app);

        DeleteAppsCore(new[] { app });

        return new AutomationResponse { Id = request.Id, Item = described };
    }

    private AutomationResponse MoveItem(AutomationRequest request)
    {
        if (Resolve(request, out var app) is { } failure) return failure;
        if (TargetFolder(request, out var folder) is { } badFolder) return badFolder;

        MoveAppsTo(new[] { app }, folder);

        return new AutomationResponse { Id = request.Id, Item = Describe(app) };
    }

    private AutomationResponse SetItemTags(AutomationRequest request)
    {
        if (Resolve(request, out var app) is { } failure) return failure;

        var snapshot = Snapshot();
        var ids = new HashSet<Guid>();

        foreach (var key in request.Tags!)
        {
            var found = ItemLookup.Tag(snapshot, key);

            if (found.Outcome == LookupOutcome.Ambiguous)
                return Failed(request, AutomationError.TagAmbiguous, found.Candidates);

            if (found.Value is null)
                return Failed(request, AutomationError.TagNotFound, detail: key);

            ids.Add(found.Value.Id);
        }

        // Through the workspace order, like every other tag assignment: the dots on two tiles
        // carrying the same tags have to line up, and that is what maintains it.
        SetAppTags(app, TagOrdering.InWorkspaceOrder(_tags, ids));

        return new AutomationResponse { Id = request.Id, Item = Describe(app) };
    }

    #endregion

    #region Folders, tags and workspaces

    private AutomationResponse CreateFolder(AutomationRequest request)
    {
        var invalid = AutomationEdit.ResolveName(request.Name, out var name);
        if (invalid != AutomationError.None) return Failed(request, invalid);

        var folder = CreateFolderCore(name);

        return new AutomationResponse { Id = request.Id, Folder = Describe(folder) };
    }

    private AutomationResponse RenameFolder(AutomationRequest request)
    {
        if (ResolveFolder(request, request.Folder, out var folder) is { } failure) return failure;

        var invalid = AutomationEdit.ResolveName(request.Name, out var name);
        if (invalid != AutomationError.None) return Failed(request, invalid);

        RenameFolderCore(folder, name);

        return new AutomationResponse { Id = request.Id, Folder = Describe(folder) };
    }

    private AutomationResponse DeleteFolder(AutomationRequest request)
    {
        if (ResolveFolder(request, request.Folder, out var folder) is { } failure) return failure;

        var described = Describe(folder);

        // The items inside go with it, as they do from the rail. The tool says so.
        DeleteFolderCore(folder);

        return new AutomationResponse { Id = request.Id, Folder = described };
    }

    private AutomationResponse CreateTag(AutomationRequest request)
    {
        var invalidName = AutomationEdit.ResolveName(request.Name, out var name);
        if (invalidName != AutomationError.None) return Failed(request, invalidName);

        var invalidColor = AutomationEdit.ResolveColor(request.Color, out var color);
        if (invalidColor != AutomationError.None) return Failed(request, invalidColor);

        // A name already in use is allowed, because it is allowed in the manage dialog, and
        // automation is not the place to invent a rule the UI does not have. The cost lands on
        // whoever then names that tag in a request and is told it is ambiguous.
        var tag = new TagViewModel(name, color);
        _tags.Add(tag);
        CommitSave();

        return new AutomationResponse
        {
            Id = request.Id,
            Tag = new TagSummary(tag.Id, tag.Name, tag.ColorKey, _currentWorkspace.Name)
        };
    }

    private async Task<AutomationResponse> SwitchToWorkspaceAsync(AutomationRequest request)
    {
        var found = ItemLookup.Workspace(_workspaceConfig, request.Workspace);

        if (found.Outcome == LookupOutcome.Ambiguous)
            return Failed(request, AutomationError.WorkspaceAmbiguous, found.Candidates);

        if (found.Value is null)
            return Failed(request, AutomationError.WorkspaceNotFound);

        // SwitchWorkspaceAsync returns early when it is already open, so this is a no-op that
        // still answers with the workspace the caller asked about.
        await SwitchWorkspaceFromCodeAsync(found.Value);

        return new AutomationResponse
        {
            Id = request.Id,
            Workspace = new WorkspaceSummary(
                found.Value.Id, found.Value.Name, found.Value.ColorTag, found.Value.AppCount, true)
        };
    }

    #endregion

    #region Window and history

    private AutomationResponse ShowWindow(AutomationRequest request)
    {
        AppWindow.Show();

        // The same path the tray and the hotkey use. Activate() alone cannot raise a window
        // from a process that is not already in the foreground, which is exactly this case.
        ((App)Application.Current).BringToForeground();

        return AutomationResponse.Done(request.Id);
    }

    /// <summary>
    /// Hides the window, unless hiding it would be the end of it.
    /// </summary>
    /// <remarks>
    /// With no tray icon and no global hotkey there is nothing left to bring the window back —
    /// not even <c>show_window</c>, since the next client may not come. The app has the same
    /// guard on close-to-tray for the same reason, and refusing is better than an app the user
    /// can only recover by restarting it.
    /// </remarks>
    private AutomationResponse HideWindow(AutomationRequest request)
    {
        if (!App.TrayEnabled && Settings.Hotkey is null)
            return Failed(request, AutomationError.NoWayToRestore);

        AppWindow.Hide();

        return AutomationResponse.Done(request.Id);
    }

    private AutomationResponse ClearRecents(AutomationRequest request) =>
        new() { Id = request.Id, Count = ClearRecentLaunches() };

    #endregion

    #region Resolution and projection

    /// <summary>
    /// Finds the item a request names, by the shared rules.
    /// </summary>
    /// <remarks>
    /// Through <see cref="ItemLookup"/> on a snapshot rather than a search of its own, so a name
    /// resolves here exactly as it does in the read-only tools — including refusing to choose
    /// when two items answer to it. The id then comes back to a view model, because the view
    /// models are what the collections hold.
    /// </remarks>
    private AutomationResponse? Resolve(AutomationRequest request, out AppItemViewModel app)
    {
        app = null!;

        var found = ItemLookup.Item(Snapshot(), request.Item);

        if (found.Outcome == LookupOutcome.Ambiguous)
            return Failed(request, AutomationError.ItemAmbiguous, found.Candidates);

        // The snapshot was built from these collections a moment ago on this same thread, so
        // the second step cannot fail — but it is the step that turns a stored item back into
        // the instance the UI is bound to, and editing a detached copy would change nothing.
        if (found.Value is not { } stored || FindAppById(stored.Id) is not { } resolved)
            return Failed(request, AutomationError.ItemNotFound, detail: request.Item);

        app = resolved;
        return null;
    }

    /// <summary>
    /// Where an add or a move should land: the folder the request names, or ungrouped when it
    /// names none. An unknown name is an error rather than a fall back to ungrouped — putting
    /// an item somewhere other than where the caller said is worse than not putting it anywhere.
    /// </summary>
    private AutomationResponse? TargetFolder(AutomationRequest request, out FolderViewModel? folder)
    {
        folder = null;

        if (string.IsNullOrWhiteSpace(request.Folder)) return null;

        var failure = ResolveFolder(request, request.Folder, out var resolved);
        if (failure is not null) return failure;

        folder = resolved;
        return null;
    }

    private AutomationResponse? ResolveFolder(
        AutomationRequest request,
        string? key,
        out FolderViewModel folder)
    {
        folder = null!;

        var found = ItemLookup.Folder(Snapshot(), key);

        if (found.Outcome == LookupOutcome.Ambiguous)
            return Failed(request, AutomationError.FolderAmbiguous, found.Candidates);

        if (found.Value is not { } stored
            || _folders.FirstOrDefault(f => f.Id == stored.Id) is not { } resolved)
            return Failed(request, AutomationError.FolderNotFound, detail: key);

        folder = resolved;
        return null;
    }

    /// <summary>
    /// One item as the automation interface reports it, through the same projection the queries
    /// use — so a client never has to reconcile two descriptions of one row.
    /// </summary>
    private ItemSummary Describe(AppItemViewModel app)
    {
        var snapshot = Snapshot();

        return AutomationQuery.Describe(
            snapshot,
            app.ToModel(),
            FindFolderOfApp(app)?.DisplayName,
            _currentWorkspace.Name);
    }

    private FolderSummary Describe(FolderViewModel folder) =>
        new(folder.Id, folder.DisplayName, folder.Apps.Count, _currentWorkspace.Name);

    private static AutomationResponse Failed(
        AutomationRequest request,
        AutomationError error,
        IReadOnlyList<LookupCandidate>? candidates = null,
        string? detail = null) =>
        AutomationResponse.Failed(request.Id, error, candidates, detail);

    #endregion
}
