using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TestPulse.MSBuild.Tests;

/// <summary>
/// A minimal real HTTP server (HttpListener, not a mock) for the real
/// end-to-end tests to submit against, matching the same "real server,
/// real subprocess" rigor used for the rest of this plugin's e2e tests.
/// </summary>
internal sealed class StubServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;

    public string Url { get; }
    public string? LastRequestBody { get; private set; }
    public string LastRequestPath { get; private set; } = "";

    public Func<string, (int StatusCode, string Body)> Handler { get; set; } = _ => (201, "{\"id\":\"r1\",\"key\":\"RUN-1\"}");

    public StubServer()
    {
        var port = GetFreePort();
        Url = $"http://127.0.0.1:{port}/";
        _listener = new HttpListener();
        _listener.Prefixes.Add(Url);
        _listener.Start();
        _loop = Task.Run(AcceptLoop);
    }

    private static int GetFreePort()
    {
        using var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    private async Task AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync();
            }
            catch
            {
                return;
            }

            LastRequestPath = ctx.Request.Url?.AbsolutePath ?? "";
            using var reader = new System.IO.StreamReader(ctx.Request.InputStream);
            LastRequestBody = await reader.ReadToEndAsync();

            var (status, body) = Handler(LastRequestPath);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json";
            var bytes = Encoding.UTF8.GetBytes(body);
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close();
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        _listener.Close();
    }
}
