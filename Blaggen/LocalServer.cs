using System.Collections.Immutable;
using System.Net;
using System.Text;
using Spectre.Console;

namespace Blaggen;

public class ServerVfs : VfsWrite
{
    public ServerVfs(DirectoryInfo root)
    {
        this.root = root.GetDir("public");
    }

    private readonly DirectoryInfo root;
    private readonly Dictionary<string, string> content = new();

    internal string? GetContent(string url)
    {
        var split = url.Split('/', StringSplitOptions.RemoveEmptyEntries).ToImmutableArray();
        var pathToFileSplit = url.EndsWith('/') ? split.Concat(new[] { "index.html" }) : split;

        var relativePath = string.Join(Path.DirectorySeparatorChar, pathToFileSplit);

        var fullPath = Path.Join(root.FullName, relativePath);
        if (content.TryGetValue(fullPath, out var fileData) == false)
        {
            // AnsiConsole.WriteLine($"Generated path {fullPath} for {relativePath} in directory {Root.FullName}");
            return null;
        }

        return fileData;
    }

    public Task WriteAllTextAsync(FileInfo path, string contents)
    {
        return Task.Factory.StartNew(() => { content[path.FullName] = contents; });
    }
}

internal class LocalServer
{
    private static async Task HandleIncomingConnections(Run run, ServerVfs vfs, HttpListener listener, CancellationToken ct)
    {
        while (ct.IsCancellationRequested == false)
        {
            var context = await listener.GetContextAsync();
            var request = context.Request;
            var response = context.Response;

            var url = request.Url?.AbsolutePath ?? "<null uri>";

            var html = vfs.GetContent(url);

            // todo(Gustav): use extension to switch MIME
            // todo(Gustav): read static files from real directory

            if(html == null)
            {
                AnsiConsole.MarkupLineInterpolated($"missing url: {url}");

                // todo(Gustav): generate a better 404
                html = $@"<!DOCTYPE>
                <html>
                  <head>
                    <title>404: missing path</title>
                  </head>
                  <body>
                    <p>Missing path {url}</p>
                  </body>
                </html>";
            }

            var encoding = Encoding.UTF8;
            var data = encoding.GetBytes(html);
            response.ContentType = "text/html";
            response.ContentEncoding = encoding;
            response.ContentLength64 = data.LongLength;

            try
            {
                await response.OutputStream.WriteAsync(data, 0, data.Length, ct);
                response.Close();
            }
            catch (Exception ex)
            {
                AnsiConsole.WriteException(ex);
            }
        }
    }


    internal static async Task<int> Run(Run run, ServerVfs vfs, int port, CancellationToken ct)
    {
        var url = $"http://localhost:{port}/";
        var listener = new HttpListener();
        listener.Prefixes.Add(url);
        listener.Start();
        run.Status($"Listening for connections on {url}");
        
        await HandleIncomingConnections(run, vfs, listener, ct);

        listener.Close();
        return 0;
    }
}