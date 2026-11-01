using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace ace_run.Mcp;

internal static class Program
{
    /// <summary>
    /// What an MCP client is told the server is for. Worth spending words on: it is the only
    /// place a client learns that these answers come from the files on disk rather than from
    /// the running window, and when that difference can show.
    /// </summary>
    private const string Instructions = """
        Ace Run is a Windows launcher. A workspace holds launch items — executables with
        optional arguments, and URLs or custom protocols — either ungrouped or inside folders,
        and tagged.

        These tools are the way to read that data. Do not open the files under
        %LOCALAPPDATA%\AceRun yourself: the tools project the same contents, already resolve
        tag ids to names, and will keep working when the storage format changes.

        Tags and folders belong to a workspace, not to the app. A tag name that exists in one
        workspace may not exist in another, so pass workspace 'all' to find where one lives,
        then narrow. To answer "which items have tag X", call list_items with that tag rather
        than listing everything and filtering.

        Listings are paged and report a total. To read the rest, pass the page's own
        'nextOffset' back as 'offset' rather than working out where the next page starts; when
        'nextOffset' is absent you have everything. If the total is large, narrow with tag or
        folder instead of paging through it.

        Questions are answered whether or not Ace Run is running: the server asks the app for
        its live state and reads the data files when it cannot. So list_* and search_* always
        work.

        Launching and editing go through the running app, which owns the live data, and are
        switched off by default — the user turns them on under Settings > Automation, where
        there is also a read-only position. If one of those tools reports that, say so rather
        than looking for another way in; editing the files directly would be overwritten by the
        app's next save.

        The tools that act address the workspace Ace Run has open, and only that one. Reading
        spans every workspace, so an item you found with workspace 'all' may well need a
        switch_workspace call before you can launch it.

        An item is an executable or a URL for life, and a file has to exist before it can be
        added. delete_item and delete_folder do not ask for confirmation on the app side and
        cannot be undone; deleting a folder deletes the items in it.
        """;

    /// <summary>
    /// The SDK's own serializer settings, with one change: non-ASCII characters are written as
    /// themselves instead of as <c>\uXXXX</c> escapes.
    /// </summary>
    /// <remarks>
    /// Not cosmetic. Every escape costs six bytes per character, so a library of Japanese
    /// titles and paths — which is what one of these workspaces is — serializes to roughly
    /// twice the size it needs to, and the size of a tool result is what decides whether the
    /// client that asked can actually read it. The app's own files are written the same way,
    /// for the same reason (<c>AceRunJson</c>).
    /// </remarks>
    private static readonly JsonSerializerOptions ToolJson =
        new(McpJsonUtilities.DefaultOptions) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // stdout *is* the protocol channel. The default console logger writes there, which
        // interleaves log lines with JSON-RPC messages and breaks the session — so the
        // providers are replaced with one pinned to stderr, where a client shows them as
        // server output instead of trying to parse them.
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

        builder.Services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation
                {
                    Name = "ace-run",
                    Title = "Ace Run",
                    Version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"
                };
                options.ServerInstructions = Instructions;
            })
            .WithStdioServerTransport()
            .WithToolsFromAssembly(serializerOptions: ToolJson);

        await builder.Build().RunAsync();
    }
}
