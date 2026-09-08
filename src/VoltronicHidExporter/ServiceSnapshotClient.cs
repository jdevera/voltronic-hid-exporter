using System.ComponentModel;
using System.Net.Http;
using System.ServiceProcess;

namespace VoltronicHidExporter;

public static class ServiceSnapshotClient
{
    private const int ServiceDoesNotExist = 1060;

    public static async Task<ServiceSnapshotResult?> TryReadAsync(
        ExporterOptions options,
        CancellationToken cancellationToken = default)
    {
        using var handler = new HttpClientHandler { UseProxy = false };
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(2),
        };

        try
        {
            using var response = await client.GetAsync(
                BuildLoopbackUri(options.ListenPrefix, "/snapshot"),
                cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return new ServiceSnapshotResult(response.IsSuccessStatusCode, body.TrimEnd());
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    public static ServiceActivity GetServiceActivity(string serviceName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return ServiceActivity.StoppedOrAbsent;
        }

        try
        {
            using var service = new ServiceController(serviceName);
            return service.Status == ServiceControllerStatus.Stopped
                ? ServiceActivity.StoppedOrAbsent
                : ServiceActivity.Active;
        }
        catch (InvalidOperationException exception)
            when (exception.InnerException is Win32Exception { NativeErrorCode: ServiceDoesNotExist })
        {
            return ServiceActivity.StoppedOrAbsent;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == ServiceDoesNotExist)
        {
            return ServiceActivity.StoppedOrAbsent;
        }
        catch
        {
            return ServiceActivity.Unknown;
        }
    }

    public static Uri BuildLoopbackUri(string listenPrefix, string path)
    {
        var parseablePrefix = listenPrefix
            .Replace("http://+:", "http://127.0.0.1:", StringComparison.OrdinalIgnoreCase)
            .Replace("http://*:", "http://127.0.0.1:", StringComparison.OrdinalIgnoreCase)
            .Replace("https://+:", "https://127.0.0.1:", StringComparison.OrdinalIgnoreCase)
            .Replace("https://*:", "https://127.0.0.1:", StringComparison.OrdinalIgnoreCase);
        var builder = new UriBuilder(parseablePrefix)
        {
            Host = "127.0.0.1",
            Path = path,
            Query = string.Empty,
        };
        return builder.Uri;
    }
}

public sealed record ServiceSnapshotResult(bool Success, string Body);

public enum ServiceActivity
{
    StoppedOrAbsent,
    Active,
    Unknown,
}
