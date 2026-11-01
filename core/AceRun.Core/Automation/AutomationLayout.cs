using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ace_run.Services;

namespace ace_run.Automation;

/// <summary>
/// The one assumption the two halves of the automation interface make about each other: they
/// are published into the same folder.
/// </summary>
/// <remarks>
/// <para>
/// That is what lets the MCP server start the app when it is not running, and the settings
/// window write out a client configuration with a real path in it — neither of them guessing an
/// install location, reading the registry, or needing the user to find the file.
/// </para>
/// <para>
/// It also means neither half works that way from a development build, where each project has
/// its own <c>bin</c>. Both callers handle the miss by saying so rather than by searching.
/// </para>
/// </remarks>
public static class AutomationLayout
{
    public const string McpExeName = "AceRun.Mcp.exe";
    public const string AppExeName = "ace-run.exe";

    /// <summary>
    /// The switch that starts the app without putting its window on screen.
    /// </summary>
    /// <remarks>
    /// Here rather than only in <c>StartupService</c>, which exposes it under its own name, so
    /// that the MCP server can start the app the same way the Run key does. An agent that needs
    /// the app running asked to launch something, not to be shown a window.
    /// </remarks>
    public const string TrayArgument = "--tray";

    /// <summary>
    /// The named executable in <paramref name="directory"/>, or null when it is not there.
    /// </summary>
    public static string? Sibling(string? directory, string exeName)
    {
        if (string.IsNullOrEmpty(directory)) return null;

        try
        {
            var path = Path.Combine(directory, exeName);
            return File.Exists(path) ? path : null;
        }
        catch (Exception)
        {
            // A base directory that is not a usable path at all. Nothing to find.
            return null;
        }
    }

    /// <summary>
    /// A ready-to-paste MCP client configuration naming <paramref name="mcpExePath"/>.
    /// </summary>
    /// <remarks>
    /// The <c>mcpServers</c> shape, which is what Claude Desktop and Claude Code take. VS Code
    /// wants the same entry under <c>servers</c> with a <c>type</c> of <c>stdio</c>; that one is
    /// in the README rather than here, because a clipboard can only hold one answer and this is
    /// the one more clients accept.
    /// <para>
    /// Built through the serializer rather than by string interpolation: the value is a Windows
    /// path, and every backslash in it has to be doubled.
    /// </para>
    /// </remarks>
    public static string ClientConfigJson(string mcpExePath) =>
        JsonSerializer.Serialize(
            new Dictionary<string, object>
            {
                ["mcpServers"] = new Dictionary<string, object>
                {
                    ["ace-run"] = new Dictionary<string, object>
                    {
                        ["command"] = mcpExePath
                    }
                }
            },
            AceRunJson.Options);
}
