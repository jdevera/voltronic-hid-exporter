using System.Globalization;
using System.Text.Json;

namespace VoltronicHidExporter;

public sealed class RawCaptureWriter(
    ExporterOptions options,
    ILogger<RawCaptureWriter> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private DateOnly? _lastCleanupDate;

    public Task WriteSuccessAsync(PollSnapshot snapshot, CancellationToken cancellationToken) =>
        AppendAsync(new
        {
            timestamp = snapshot.Timestamp,
            success = true,
            snapshot.DevicePath,
            snapshot.Protocol,
            snapshot.Ratings,
            rawQueryStatus = snapshot.RawQueryStatus,
            snapshot.Telemetry,
            snapshot.WindowsBattery,
        }, snapshot.Timestamp, cancellationToken);

    public Task WriteFailureAsync(string error, CancellationToken cancellationToken) =>
        AppendAsync(new
        {
            timestamp = DateTimeOffset.UtcNow,
            success = false,
            error,
        }, DateTimeOffset.UtcNow, cancellationToken);

    private async Task AppendAsync(
        object record,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken)
    {
        if (options.RawCaptureRetentionDays <= 0)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(options.RawCaptureDirectory);
            var filename = $"ups-{timestamp.UtcDateTime:yyyy-MM-dd}.jsonl";
            var path = Path.Combine(options.RawCaptureDirectory, filename);
            var line = JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine;

            // Open, append, and close each record so recent samples survive an
            // unexpected shutdown during a power cut.
            await File.AppendAllTextAsync(path, line, cancellationToken);
            CleanupIfNeeded(DateOnly.FromDateTime(timestamp.UtcDateTime));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not append raw UPS capture data");
        }
    }

    private void CleanupIfNeeded(DateOnly today)
    {
        if (_lastCleanupDate == today)
        {
            return;
        }

        _lastCleanupDate = today;
        var oldestRetainedDate = today.AddDays(-(options.RawCaptureRetentionDays - 1));
        foreach (var path in Directory.EnumerateFiles(options.RawCaptureDirectory, "ups-*.jsonl"))
        {
            var stem = Path.GetFileNameWithoutExtension(path);
            if (stem.Length != 14 ||
                !DateOnly.TryParseExact(
                    stem[4..],
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var fileDate) ||
                fileDate >= oldestRetainedDate)
            {
                continue;
            }

            File.Delete(path);
        }
    }
}
