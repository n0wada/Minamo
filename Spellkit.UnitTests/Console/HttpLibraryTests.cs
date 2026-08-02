using Spellkit.Hosting;
using Spellkit.Library;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Spellkit.UnitTesting.Cli;

public sealed class HttpLibraryTests
{
    [Fact]
    public async Task SendsGetRequestsWithParamsAndHeaders()
    {
        using var server = await OneShotHttpServer.StartAsync();
        var script = $$"""
            import * from http

            let res = Get("{{server.BaseUrl}}users",
                params: [q: "spell kit", page: 2],
                headers: ["X-Test": "yes"])
            let body = res.Json()

            fmt("{0}|{1}|{2}|{3}|{4}|{5}|{6}", res.Ok, res.StatusCode, body["method"], body["path"], body["query"]["q"], body["query"]["page"], body["headers"]["X-Test"])
            """;

        using var instance = new SpellkitHost()
            .AddStandardLibrary()
            .AddExtendedLibrary()
            .CreateInstance();
        var result = instance.Execute(script);

        Assert.True(result.Success, result.Failure?.Message);
        Assert.Equal("true|200|GET|/users|spell kit|2|yes", result.GetValue<string>());
    }

    [Fact]
    public async Task SessionAppliesDefaultsAndSendsJsonBodies()
    {
        using var server = await OneShotHttpServer.StartAsync();
        var script = $$"""
            import * from http

            let session = Session(
                baseUrl: "{{server.BaseUrl}}",
                headers: ["Accept": "application/json"],
                auth: [bearer: "secret-token"])
            let res = session.Post("orders", json: [id: 42, customer: "Ada"])
            let body = res.RaiseForStatus().Json()

            fmt("{0}|{1}|{2}|{3}|{4}|{5}", body["method"], body["path"], body["headers"]["Accept"], body["headers"]["Authorization"], body["body"]["id"], body["body"]["customer"])
            """;

        using var instance = new SpellkitHost()
            .AddStandardLibrary()
            .AddExtendedLibrary()
            .CreateInstance();
        var result = instance.Execute(script);

        Assert.True(result.Success, result.Failure?.Message);
        Assert.Equal("POST|/orders|application/json|Bearer secret-token|42|Ada", result.GetValue<string>());
    }

    private sealed class OneShotHttpServer : IDisposable
    {
        private readonly TcpListener listener;
        private readonly Task serveTask;

        private OneShotHttpServer(TcpListener listener)
        {
            this.listener = listener;
            BaseUrl = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";
            serveTask = Task.Run(ServeAsync);
        }

        public string BaseUrl { get; }

        public static Task<OneShotHttpServer> StartAsync()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return Task.FromResult(new OneShotHttpServer(listener));
        }

        public void Dispose()
        {
            listener.Stop();
            try
            {
                serveTask.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
            }
        }

        private async Task ServeAsync()
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            var request = await ReadRequestAsync(stream);
            var body = JsonSerializer.Serialize(new
            {
                method = request.Method,
                path = request.Path,
                query = request.Query,
                headers = request.Headers,
                body = request.Body.Length == 0
                    ? null
                    : JsonSerializer.Deserialize<object>(request.Body)
            });
            var payload = Encoding.UTF8.GetBytes(body);
            var responseHeader = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\n"
                + "Content-Type: application/json\r\n"
                + $"Content-Length: {payload.Length}\r\n"
                + "Connection: close\r\n\r\n");

            await stream.WriteAsync(responseHeader);
            await stream.WriteAsync(payload);
        }

        private static async Task<Request> ReadRequestAsync(NetworkStream stream)
        {
            var buffer = new byte[8192];
            var bytes = new List<byte>();
            var headerEnd = -1;

            while (headerEnd < 0)
            {
                var read = await stream.ReadAsync(buffer);
                if (read == 0)
                {
                    break;
                }

                bytes.AddRange(buffer.AsSpan(0, read).ToArray());
                headerEnd = IndexOfHeaderEnd(bytes);
            }

            var headerText = Encoding.ASCII.GetString(bytes.Take(headerEnd).ToArray());
            var lines = headerText.Split("\r\n", StringSplitOptions.None);
            var requestLine = lines[0].Split(' ');
            var headers = lines.Skip(1)
                .Where(line => line.Length > 0)
                .Select(line => line.Split(':', 2))
                .ToDictionary(
                    parts => parts[0],
                    parts => parts.Length > 1 ? parts[1].Trim() : "",
                    StringComparer.OrdinalIgnoreCase);
            var contentLength = headers.TryGetValue("Content-Length", out var lengthText)
                && int.TryParse(lengthText, out var length)
                    ? length
                    : 0;
            var bodyStart = headerEnd + 4;
            while (bytes.Count - bodyStart < contentLength)
            {
                var read = await stream.ReadAsync(buffer);
                if (read == 0)
                {
                    break;
                }

                bytes.AddRange(buffer.AsSpan(0, read).ToArray());
            }

            var target = new Uri("http://127.0.0.1" + requestLine[1]);
            var query = target.Query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split('=', 2))
                .ToDictionary(
                    parts => Uri.UnescapeDataString(parts[0]),
                    parts => parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "",
                    StringComparer.Ordinal);
            var body = Encoding.UTF8.GetString(bytes.Skip(bodyStart).Take(contentLength).ToArray());

            return new Request(requestLine[0], target.AbsolutePath, query, headers, body);
        }

        private static int IndexOfHeaderEnd(List<byte> bytes)
        {
            for (var i = 3; i < bytes.Count; i++)
            {
                if (bytes[i - 3] == '\r'
                    && bytes[i - 2] == '\n'
                    && bytes[i - 1] == '\r'
                    && bytes[i] == '\n')
                {
                    return i - 3;
                }
            }

            return -1;
        }

        private sealed record Request(
            string Method,
            string Path,
            Dictionary<string, string> Query,
            Dictionary<string, string> Headers,
            string Body);
    }
}
