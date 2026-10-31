using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using ace_run.Models;
using ace_run.Services;

namespace ace_run.Automation;

/// <summary>How a read of one of AceRun's data files turned out.</summary>
public enum ReadOutcome
{
    Ok,

    /// <summary>No such file. For a workspace file that means the id names no workspace.</summary>
    Missing,

    /// <summary>The file is there but could not be read — still locked after every try, or no access.</summary>
    Unreadable,

    /// <summary>The file was read but does not deserialize into the requested type.</summary>
    Malformed
}

public readonly record struct ReadResult<T>(ReadOutcome Outcome, T? Value) where T : class
{
    /// <summary>True only when there is a value to use; <c>Value</c> is null in every other case.</summary>
    public bool Ok => Outcome == ReadOutcome.Ok && Value is not null;
}

/// <summary>
/// Reads the config and workspace files for a caller that has to be able to answer
/// "I could not read it" — the automation server, which answers questions about the data
/// rather than starting the app on top of it.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not <see cref="DataStore"/>. Its loads go through <c>ReadOrDefault</c>, which
/// answers every failure with an empty object. That is right for the app's own startup —
/// starting empty beats not starting — but it is wrong here: an empty <see cref="AppData"/> is
/// indistinguishable from a workspace that genuinely has nothing in it, so a locked file would
/// be reported to the caller as the confident, false answer "you have no items". The same
/// reasoning rules out <see cref="DataStore.ParseAppData"/> for the parse step; it cannot
/// report failure either, so this deserializes through <see cref="AceRunJson.Options"/> itself.
/// </para>
/// <para>
/// The share mode is the other half of why this is not <see cref="File.ReadAllText(string)"/>.
/// That opens with <c>FileShare.Read</c>, which denies write and delete access for as long as
/// the handle lives; <see cref="OpenShared"/> grants both, so a save landing in the same
/// instant can still open the file and write it.
/// </para>
/// <para>
/// What no share mode can do is let a rename-over succeed.
/// <see cref="DataStore.SaveWorkspace"/> finishes with
/// <see cref="File.Move(string,string,bool)"/> onto this path, and on Windows that fails with
/// access denied while <em>any</em> handle to the destination is open: <c>FILE_SHARE_DELETE</c>
/// only permits the deletion to be pending, and a pending deletion keeps the directory entry
/// the rename needs. Measured, not assumed — the first version of this comment claimed the
/// opposite and a test said no. So the read window is one open-read-close with nothing in
/// between, which is also the best this side can do about it; the retry below is for the
/// mirror case, our read arriving while a save holds the file.
/// </para>
/// </remarks>
public sealed class AutomationReader
{
    private readonly AceRunPaths _paths;
    private readonly int _attempts;
    private readonly int _retryDelayMs;

    /// <param name="attempts">
    /// Total tries, not extra ones. More than one because the expected failure is transient:
    /// something else holding the file while a save replaces it.
    /// </param>
    /// <param name="retryDelayMs">Pause between tries. Tests pass 0.</param>
    public AutomationReader(AceRunPaths paths, int attempts = 3, int retryDelayMs = 50)
    {
        if (attempts < 1) throw new ArgumentOutOfRangeException(nameof(attempts));

        _paths = paths;
        _attempts = attempts;
        _retryDelayMs = retryDelayMs;
    }

    /// <summary>Reads the real profile, like <see cref="DataStore.Default"/>.</summary>
    public static AutomationReader Default { get; } = new(AceRunPaths.Default);

    public AceRunPaths Paths => _paths;

    public ReadResult<WorkspaceConfig> ReadConfig() => Read<WorkspaceConfig>(_paths.ConfigFile);

    public ReadResult<AppData> ReadWorkspace(Guid id) => Read<AppData>(_paths.WorkspaceFile(id));

    /// <summary>
    /// Opens a data file in the one share mode that cannot break a concurrent atomic save.
    /// Public because that property is worth a test of its own rather than a comment alone.
    /// </summary>
    public static Stream OpenShared(string path) =>
        new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    private ReadResult<T> Read<T>(string path) where T : class
    {
        string json;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var stream = OpenShared(path);
                using var reader = new StreamReader(stream);
                json = reader.ReadToEnd();
                break;
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                // Checked by opening rather than by File.Exists first: the answer to that
                // question goes stale between the two calls, and this is the same answer.
                return new ReadResult<T>(ReadOutcome.Missing, null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt >= _attempts)
                    return new ReadResult<T>(ReadOutcome.Unreadable, null);

                if (_retryDelayMs > 0) Thread.Sleep(_retryDelayMs);
            }
        }

        try
        {
            var value = JsonSerializer.Deserialize<T>(json, AceRunJson.Options);

            // Deserialize returns null for the literal "null", which is not a usable file.
            return value is null
                ? new ReadResult<T>(ReadOutcome.Malformed, null)
                : new ReadResult<T>(ReadOutcome.Ok, value);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return new ReadResult<T>(ReadOutcome.Malformed, null);
        }
    }
}
