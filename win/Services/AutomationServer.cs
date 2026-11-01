using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using ace_run.Automation;

namespace ace_run.Services;

/// <summary>
/// The listening half of the automation interface: a named pipe that takes one JSON request per
/// line and answers one JSON response per line.
/// </summary>
/// <remarks>
/// <para>
/// A pipe rather than a socket, and certainly not HTTP. The caller is a process on this machine
/// running as this user; a pipe says exactly that in its security descriptor, needs no port, and
/// cannot be reached from the network by accident. 12-mcp-automation.md §B1.
/// </para>
/// <para>
/// This class knows nothing about what the commands mean. It reads lines, hands each one to the
/// window, and writes back what the window returns — so everything that touches the UI stays on
/// the UI thread, in <c>MainWindow.Automation.cs</c>, and the threading rule is one rule in one
/// place.
/// </para>
/// </remarks>
internal sealed class AutomationServer
{
    private readonly Func<AutomationRequest, Task<AutomationResponse>> _handle;
    private CancellationTokenSource? _stop;

    public AutomationServer(Func<AutomationRequest, Task<AutomationResponse>> handle) =>
        _handle = handle;

    public bool IsRunning => _stop is not null;

    /// <summary>
    /// Starts listening, or does nothing if it already is. Starting twice is the normal case,
    /// not a mistake: the settings window applies the whole automation setting on every change.
    /// </summary>
    public void Start()
    {
        if (_stop is not null) return;

        var stop = new CancellationTokenSource();
        _stop = stop;

        // Not awaited, and deliberately not on the UI thread: the accept loop spends its life
        // blocked on WaitForConnectionAsync.
        _ = Task.Run(() => AcceptLoopAsync(stop.Token));
    }

    /// <summary>
    /// Stops listening. Connections already open end when their client goes away or when the
    /// cancellation reaches their next read, whichever comes first.
    /// </summary>
    public void Stop()
    {
        var stop = _stop;
        _stop = null;

        if (stop is null) return;

        try { stop.Cancel(); }
        catch (Exception ex) { Debug.WriteLine($"Automation stop failed: {ex.Message}"); }

        stop.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;

            try
            {
                pipe = CreateInstance();
                await pipe.WaitForConnectionAsync(token);
            }
            catch (OperationCanceledException)
            {
                pipe?.Dispose();
                return;
            }
            catch (Exception ex)
            {
                // Most likely another process already owns the name, or the SD was refused.
                // Retrying slowly beats both spinning and giving up for the session: the app
                // outlives whatever is in the way.
                Debug.WriteLine($"Automation listen failed: {ex.Message}");
                pipe?.Dispose();

                try { await Task.Delay(TimeSpan.FromSeconds(2), token); }
                catch (OperationCanceledException) { return; }

                continue;
            }

            // Served on its own task so the loop can go straight back to accepting. More than
            // one client is the normal case — an editor and a terminal can both have an MCP
            // session open.
            _ = ServeAsync(pipe, token);
        }
    }

    /// <summary>
    /// One pipe instance, authorized to this user and nobody else.
    /// </summary>
    /// <remarks>
    /// The default descriptor would do almost the same thing, and "almost" is the problem: it
    /// also grants read access to Everyone, which is not something to leave implicit on the
    /// channel that can launch programs. <c>CreateNewInstance</c> is in the rule because once an
    /// explicit descriptor is supplied, the creator needs that right to open the next instance
    /// of the same name.
    /// </remarks>
    private static NamedPipeServerStream CreateInstance()
    {
        var security = new PipeSecurity();
        using var identity = WindowsIdentity.GetCurrent();

        security.AddAccessRule(new PipeAccessRule(
            identity.User ?? throw new InvalidOperationException("The process has no user SID."),
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            AutomationPipe.Name,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            security);
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken token)
    {
        try
        {
            using (pipe)
            {
                using var reader = new StreamReader(pipe, AutomationPipe.Encoding, false, 4096, leaveOpen: true);
                using var writer = new StreamWriter(pipe, AutomationPipe.Encoding, 4096, leaveOpen: true)
                {
                    NewLine = AutomationPipe.LineEnding,
                    AutoFlush = false
                };

                while (!token.IsCancellationRequested && pipe.IsConnected)
                {
                    var line = await reader.ReadLineAsync(token);
                    if (line is null) break;
                    if (line.Length == 0) continue;

                    await writer.WriteLineAsync(await AnswerAsync(line));

                    // Explicit, because AutoFlush is off: a buffered answer the client is
                    // blocked waiting for is a deadlock, not a delay.
                    await writer.FlushAsync(token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down, or the client went away mid-read.
        }
        catch (IOException)
        {
            // Broken pipe: the client disconnected. Ordinary, not worth a line in the log.
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Automation connection failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Turns one request line into one response line, answering even when the line itself made
    /// no sense — a client left waiting for a reply it will never get is worse than an error.
    /// </summary>
    private async Task<string> AnswerAsync(string line)
    {
        var request = AutomationPipe.TryDeserialize<AutomationRequest>(line);

        if (request is null)
            return AutomationPipe.Serialize(AutomationResponse.Failed(
                string.Empty, AutomationError.Internal, detail: "The request was not valid JSON."));

        try
        {
            return AutomationPipe.Serialize(await _handle(request));
        }
        catch (Exception ex)
        {
            // The handler runs app code on the UI thread. Whatever it threw, the caller still
            // gets an answer, and the detail is the only clue either of us will have.
            Debug.WriteLine($"Automation command failed: {ex}");

            return AutomationPipe.Serialize(AutomationResponse.Failed(
                request.Id, AutomationError.Internal, detail: ex.Message));
        }
    }
}
