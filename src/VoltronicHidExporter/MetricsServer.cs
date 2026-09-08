using System.Net;
using System.Text;
using System.Text.Json;

namespace VoltronicHidExporter;

public sealed class MetricsServer(
    ExporterOptions options,
    ExporterState state,
    ILogger<MetricsServer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add(options.ListenPrefix);
        listener.Start();
        logger.LogInformation("Metrics server listening on {ListenPrefix}", options.ListenPrefix);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var context = await listener.GetContextAsync().WaitAsync(stoppingToken);
                _ = RespondAsync(context);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal service shutdown.
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task RespondAsync(HttpListenerContext context)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath;
            switch (path)
            {
                case "/metrics":
                    await WriteAsync(
                        context.Response,
                        HttpStatusCode.OK,
                        PrometheusRenderer.Render(state.Read(), DateTimeOffset.UtcNow, options),
                        "text/plain; version=0.0.4; charset=utf-8");
                    break;
                case "/health":
                    {
                        var current = state.Read();
                        await WriteAsync(
                            context.Response,
                            current.Up ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable,
                            current.Up ? "ok\n" : "ups polling is unavailable\n",
                            "text/plain; charset=utf-8");
                        break;
                    }
                case "/snapshot":
                    {
                        if (!IsLoopback(context.Request.RemoteEndPoint?.Address))
                        {
                            await WriteAsync(
                                context.Response,
                                HttpStatusCode.Forbidden,
                                "snapshot is available only from localhost\n",
                                "text/plain; charset=utf-8");
                            break;
                        }

                        var current = state.Read();
                        if (current.LastSnapshot is null)
                        {
                            await WriteAsync(
                                context.Response,
                                HttpStatusCode.ServiceUnavailable,
                                current.LastError ?? "no successful UPS poll is available\n",
                                "text/plain; charset=utf-8");
                            break;
                        }

                        await WriteAsync(
                            context.Response,
                            HttpStatusCode.OK,
                            JsonSerializer.Serialize(
                                current.LastSnapshot,
                                new JsonSerializerOptions
                                {
                                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                                    WriteIndented = true,
                                }),
                            "application/json; charset=utf-8");
                        break;
                    }
                default:
                    await WriteAsync(
                        context.Response,
                        HttpStatusCode.NotFound,
                        "not found\n",
                        "text/plain; charset=utf-8");
                    break;
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to serve metrics request");
            context.Response.Abort();
        }
    }

    private static bool IsLoopback(IPAddress? address)
    {
        if (address is null)
        {
            return false;
        }

        return IPAddress.IsLoopback(
            address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);
    }

    private static async Task WriteAsync(
        HttpListenerResponse response,
        HttpStatusCode status,
        string body,
        string contentType)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        response.StatusCode = (int)status;
        response.ContentType = contentType;
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }
}
