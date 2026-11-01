using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using ace_run.Automation;
using ace_run.Services;
using ModelContextProtocol;

namespace ace_run.Mcp;

/// <summary>
/// The connecting half of the automation interface: one request down the pipe, one answer back,
/// and the running app started first if it has to be.
/// </summary>
/// <remarks>
/// <para>
/// Everything that writes or launches goes through here, because the app owns the live state —
/// its view models are the truth and <c>CommitSave</c> rewrites the whole workspace file from
/// them, so an outside process editing the JSON would be overwritten by the next save
/// (12-mcp-automation.md, opening section).
/// </para>
/// <para>
/// Reads go through here too, but only opportunistically: <see cref="TryReadState"/> returns
/// null instead of failing, and the caller falls back to the files on disk. That is what keeps
/// every query answerable with the app closed.
/// </para>
/// </remarks>
internal static class AutomationClient
{
    /// <summary>Set to anything non-empty to stop a command from starting the app.</summary>
    public const string NoAutoStartVariable = "ACERUN_MCP_NO_AUTOSTART";

    /// <summary>
    /// How long to wait for a pipe that should already be there. Short on purpose: it is on the
    /// path of every read, and the answer "the app is not running" is one we can serve.
    /// </summary>
    private const int ConnectTimeoutMs = 300;

    /// <summary>
    /// How long to keep trying after starting the app ourselves. A cold start of a WinUI app
    /// that has to read its configuration and build a window is seconds, not milliseconds.
    /// </summary>
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// The live state, or null when the app cannot be reached.
    /// </summary>
    /// <remarks>
    /// Never starts the app. A question is not a reason to put a program on the user's machine
    /// into memory, and the same question has a correct answer from disk.
    /// </remarks>
    public static AutomationState? TryReadState()
    {
        var pipe = TryConnect(ConnectTimeoutMs);
        if (pipe is null) return null;

        var response = Exchange(pipe, new AutomationRequest
        {
            Id = NextId(),
            Command = nameof(AutomationAction.ReadState)
        });

        // A refusal here is not worth reporting: read-only mode answers this, and anything else
        // that went wrong leaves the files on disk as the better source.
        return response?.State;
    }

    /// <summary>
    /// Sends a command and returns the answer, or throws an <see cref="McpException"/> whose
    /// message says what the caller can do about it.
    /// </summary>
    public static AutomationResponse Send(AutomationRequest request)
    {
        // Filled in here so the tools do not each have to: they build a request describing what
        // they want, and the correlation id is this layer's business.
        if (string.IsNullOrEmpty(request.Id))
            request = request with { Id = NextId() };

        var pipe = TryConnect(ConnectTimeoutMs) ?? StartAppAndConnect();

        if (pipe is null)
            throw new McpException(Unreachable());

        var response = Exchange(pipe, request)
            ?? throw new McpException(
                "Ace Run accepted the request but did not answer it. Check that the app is still "
                + "running and try again.");

        if (!response.Ok)
            throw new McpException(Describe(request, response));

        return response;
    }

    private static string NextId() => Guid.NewGuid().ToString("N")[..8];

    #region Connection

    private static NamedPipeClientStream? TryConnect(int timeoutMs)
    {
        var pipe = new NamedPipeClientStream(
            ".", AutomationPipe.Name, PipeDirection.InOut, PipeOptions.None);

        try
        {
            pipe.Connect(timeoutMs);
            return pipe;
        }
        catch (TimeoutException)
        {
            // Nothing listening: the app is closed, or automation is switched off.
            pipe.Dispose();
            return null;
        }
        catch (IOException)
        {
            // Every instance of the pipe is busy. Rare — the server accepts without limit —
            // and indistinguishable from "not available" as far as the caller is concerned.
            pipe.Dispose();
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            // A pipe of that name owned by another user. The name carries our SID, so this
            // should not happen; refusing to talk to it is the only safe response.
            pipe.Dispose();
            return null;
        }
    }

    /// <summary>
    /// Starts the app in the tray and waits for its pipe to appear.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>--tray</c> rather than a plain start: the caller asked to launch something, not to be
    /// shown a window. The app is single-instance (<c>AppInstance.FindOrRegisterForKey</c>), so
    /// this cannot produce a second copy even if two clients race.
    /// </para>
    /// <para>
    /// An app that is already running is not started again, and that is not an optimisation. No
    /// pipe while the app is up means automation is switched off, and starting it then does
    /// something worse than nothing: the single-instance redirect brings the user's window to
    /// the foreground uninvited, and the caller still waits out the whole
    /// <see cref="StartupTimeout"/> for a pipe that was never going to appear. Measured at 21
    /// seconds before this check existed.
    /// </para>
    /// </remarks>
    private static NamedPipeClientStream? StartAppAndConnect()
    {
        if (AppIsRunning()) return null;
        if (!TryStartApp()) return null;

        var deadline = DateTime.UtcNow + StartupTimeout;

        while (DateTime.UtcNow < deadline)
        {
            // Each failed attempt costs its own timeout, which is what paces the loop — there
            // is no sleep here on purpose.
            if (TryConnect(500) is { } pipe) return pipe;
        }

        return null;
    }

    /// <summary>
    /// Whether an Ace Run process is up, by executable name.
    /// </summary>
    /// <remarks>
    /// A coarse check on purpose: it decides the wording of a refusal and whether to bother
    /// starting the app, nothing more. It also answers true for an instance this user cannot
    /// talk to — elevated, or in another session — and the advice is the same either way, since
    /// the pipe is per-user and the thing to check is the setting.
    /// </remarks>
    private static bool AppIsRunning()
    {
        try
        {
            var name = Path.GetFileNameWithoutExtension(AutomationLayout.AppExeName);
            return Process.GetProcessesByName(name).Length > 0;
        }
        catch
        {
            // Enumerating processes can be refused outright; then we are no worse off than
            // before this check existed.
            return false;
        }
    }

    private static bool TryStartApp()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(NoAutoStartVariable)))
            return false;

        var exe = AutomationLayout.Sibling(AppContext.BaseDirectory, AutomationLayout.AppExeName);
        if (exe is null) return false;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = AutomationLayout.TrayArgument,
                UseShellExecute = true
            });

            return true;
        }
        catch (Exception ex)
        {
            // Reported through the unreachable message, which is what the caller sees anyway.
            Debug.WriteLine($"Could not start Ace Run: {ex.Message}");
            return false;
        }
    }

    private static AutomationResponse? Exchange(NamedPipeClientStream pipe, AutomationRequest request)
    {
        try
        {
            using (pipe)
            {
                using var writer = new StreamWriter(pipe, AutomationPipe.Encoding, 4096, leaveOpen: true)
                {
                    NewLine = AutomationPipe.LineEnding,
                    AutoFlush = false
                };
                using var reader = new StreamReader(pipe, AutomationPipe.Encoding, false, 4096, leaveOpen: true);

                writer.WriteLine(AutomationPipe.Serialize(request));
                writer.Flush();

                var line = reader.ReadLine();
                return line is null ? null : AutomationPipe.TryDeserialize<AutomationResponse>(line);
            }
        }
        catch (IOException)
        {
            // The app went away mid-exchange — closed, or crashed.
            return null;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    #endregion

    #region Messages

    /// <remarks>
    /// Names the setting by what the user sees in the settings window, because that is who will
    /// have to change it, and the agent reading this has to be able to tell them where to go.
    /// </remarks>
    private static string Unreachable()
    {
        // The app is up and there is no pipe, which leaves one explanation: the interface is
        // off. Saying so is the whole point of the check in StartAppAndConnect — before it, this
        // case reported a 20-second startup that had never been needed.
        if (AppIsRunning())
            return "Ace Run is running, but its automation interface is not listening, which "
                + "means it is switched off. Ask the user to set Settings → Automation to "
                + "\"Launch and edit\"; queries keep working either way, from the data files.";

        var sameFolder = AutomationLayout.Sibling(
            AppContext.BaseDirectory, AutomationLayout.AppExeName) is not null;

        var cannotStart = string.IsNullOrEmpty(
            Environment.GetEnvironmentVariable(NoAutoStartVariable)) && sameFolder
            ? "Ace Run was started but did not come up within 20 seconds."
            : "Ace Run does not appear to be running.";

        return cannotStart
            + " Launching and editing go through the running app, which owns the live data."
            + " Ask the user to start Ace Run and set Settings → Automation to"
            + " \"Launch and edit\"; queries keep working either way, from the data files.";
    }

    /// <summary>
    /// One refusal, as a sentence that says what to do next.
    /// </summary>
    /// <remarks>
    /// The app sends a value, not a message (<see cref="AutomationError"/> explains why), so the
    /// wording is assembled here — the side that knows the names of the tools the caller could
    /// use instead.
    /// </remarks>
    private static string Describe(AutomationRequest request, AutomationResponse response) =>
        response.Error switch
        {
            AutomationError.Disabled =>
                "Ace Run has automation switched off. Ask the user to set Settings → Automation "
                + "to \"Launch and edit\".",

            AutomationError.ReadOnly =>
                "Ace Run has automation set to read-only, so it will answer questions but change "
                + "nothing. Ask the user to set Settings → Automation to \"Launch and edit\".",

            AutomationError.Busy =>
                "Ace Run has a dialog or menu open and is not accepting changes while the user is "
                + "in it. Try again shortly.",

            AutomationError.MissingArgument =>
                $"The {request.Command} request is missing an argument it needs.",

            AutomationError.UnknownCommand =>
                $"This version of Ace Run does not support '{request.Command}'.",

            AutomationError.WorkspaceNotFound =>
                $"No workspace matches '{request.Workspace}'. Call list_workspaces to see them.",

            AutomationError.WorkspaceAmbiguous =>
                $"'{request.Workspace}' matches {Candidates(response)}. Pass one of those ids.",

            AutomationError.WorkspaceNotActive =>
                $"Ace Run has '{response.Detail}' open, not '{request.Workspace}'. Launching and "
                + "editing act on the open workspace only — call switch_workspace first.",

            AutomationError.FolderNotFound =>
                $"No folder matches '{response.Detail ?? request.Folder}' in the open workspace. "
                + "Call list_folders to see them.",

            AutomationError.FolderAmbiguous =>
                $"'{request.Folder}' matches {Candidates(response)}. Pass one of those ids.",

            AutomationError.ItemNotFound =>
                $"No item matches '{response.Detail ?? request.Item}' in the open workspace. Call "
                + "list_items or search_items to find it, and remember that launching and editing "
                + "only reach the workspace the app has open.",

            AutomationError.ItemAmbiguous =>
                $"'{request.Item}' matches {Candidates(response)}. Pass one of those ids — this is "
                + "not a tie to break, the wrong choice launches the wrong program.",

            AutomationError.TagNotFound =>
                $"The open workspace has no tag matching '{response.Detail}'. Tags belong to a "
                + "workspace; call list_tags, or create_tag to add it.",

            AutomationError.TagAmbiguous =>
                $"A tag name matches {Candidates(response)}. Pass one of those ids.",

            AutomationError.InvalidName =>
                "A name cannot be empty or only spaces.",

            AutomationError.InvalidTarget =>
                "There is no file at that path, or that URL cannot be launched. Paths are checked "
                + "before an item is created, because an item that points at nothing is one the "
                + "user has to find and delete later.",

            AutomationError.InvalidColor =>
                $"A tag colour has to be one of: {string.Join(", ", ColorKeys.All)}.",

            AutomationError.InvalidKind =>
                "kind has to be 'App' for an executable or 'Url' for a URL or custom protocol.",

            AutomationError.KindNotEditable =>
                "An item is a file item or a URL item for life, and cannot be changed from one to "
                + "the other. Delete it and add the other kind.",

            AutomationError.FieldNotApplicable =>
                "A URL item has no arguments, working directory or administrator mode: it is "
                + "launched through the shell, which ignores all three.",

            AutomationError.AdminLaunchNotAllowed =>
                "That item runs as administrator, and Ace Run refuses elevated launches from "
                + "automation by default — they raise a UAC prompt the user did not ask for. Ask "
                + "them to enable \"Allow administrator launches\" in Settings → Automation.",

            AutomationError.NoWayToRestore =>
                "Hiding the window was refused: with no tray icon and no global hotkey, nothing "
                + "would bring it back.",

            AutomationError.LaunchFailed =>
                $"Windows refused to start it: {response.Detail}",

            _ =>
                $"Ace Run could not complete the request{Detail(response)}."
        };

    private static string Candidates(AutomationResponse response) =>
        response.Candidates is { Count: > 0 } candidates
            ? $"{candidates.Count}: {string.Join(", ", candidates.Select(c => $"{c.Name} = {c.Id}"))}"
            : "more than one thing";

    private static string Detail(AutomationResponse response) =>
        string.IsNullOrEmpty(response.Detail) ? string.Empty : $": {response.Detail}";

    #endregion
}
