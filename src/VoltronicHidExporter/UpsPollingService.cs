namespace VoltronicHidExporter;

public sealed class UpsPollingService(
    ExporterOptions options,
    HidAccessCoordinator accessCoordinator,
    HidUpsClient client,
    WindowsBatteryReader batteryReader,
    ExporterState state,
    RawCaptureWriter rawCapture,
    ILogger<UpsPollingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var access = accessCoordinator.Acquire();
                using var session = client.Open();
                var protocol = VoltronicProtocol.ParseDialect(session.Query("M"));
                var ratings = VoltronicProtocol.ParseRatings(session.Query("F"));
                logger.LogInformation(
                    "Connected to UPS at {DevicePath}; protocol {Protocol}",
                    session.DevicePath,
                    protocol);

                while (!stoppingToken.IsCancellationRequested)
                {
                    var rawQueryStatus = session.Query("QS");
                    var telemetry = VoltronicProtocol.ParseQueryStatus(rawQueryStatus);
                    var snapshot = new PollSnapshot(
                        DateTimeOffset.UtcNow,
                        session.DevicePath,
                        protocol,
                        ratings,
                        rawQueryStatus,
                        telemetry,
                        batteryReader.Read());

                    var transition = state.RecordSuccess(snapshot);
                    await rawCapture.WriteSuccessAsync(snapshot, stoppingToken);

                    if (transition == UtilityTransition.UtilityLost)
                    {
                        logger.LogWarning(
                            "Utility power lost; input {InputVoltage} V, load {LoadPercent}%",
                            telemetry.InputVoltage,
                            telemetry.LoadPercent);
                    }
                    else if (transition == UtilityTransition.UtilityRestored)
                    {
                        logger.LogInformation("Utility power restored");
                    }

                    await Task.Delay(
                        TimeSpan.FromSeconds(Math.Max(1, options.PollIntervalSeconds)),
                        stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                state.RecordFailure(exception.Message);
                logger.LogWarning(exception, "UPS polling failed; reconnecting");
                await rawCapture.WriteFailureAsync(exception.Message, stoppingToken);

                try
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(Math.Max(1, options.ReconnectDelaySeconds)),
                        stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }
}
