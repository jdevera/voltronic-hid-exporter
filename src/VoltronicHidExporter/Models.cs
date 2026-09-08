namespace VoltronicHidExporter;

public sealed record UpsRatings(
    double NominalOutputVoltage,
    double NominalOutputCurrent,
    double NominalBatteryVoltage,
    double NominalFrequency);

public sealed record UpsTelemetry(
    double InputVoltage,
    double InputFaultVoltage,
    double OutputVoltage,
    double LoadPercent,
    double Frequency,
    double BatteryVoltage,
    double? Temperature,
    string StatusBits,
    bool OnBattery,
    bool BatteryLow,
    bool BoostOrBuck,
    bool Fault,
    bool LineInteractive,
    bool SelfTest,
    bool ShutdownPending,
    bool BeeperEnabled);

public sealed record WindowsBatterySnapshot(
    string? Status,
    ushort? BatteryStatus,
    ushort? ChargePercent,
    ulong? EstimatedRuntimeMinutes,
    string? Error);

public sealed record PollSnapshot(
    DateTimeOffset Timestamp,
    string DevicePath,
    string Protocol,
    UpsRatings Ratings,
    string RawQueryStatus,
    UpsTelemetry Telemetry,
    WindowsBatterySnapshot WindowsBattery);

public enum UtilityTransition
{
    None,
    UtilityLost,
    UtilityRestored,
}
