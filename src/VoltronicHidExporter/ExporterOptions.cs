namespace VoltronicHidExporter;

public sealed class ExporterOptions
{
    public const string SectionName = "Exporter";

    public int VendorId { get; set; } = 0x0665;

    public int ProductId { get; set; } = 0x5161;

    public string InterfaceToken { get; set; } = "mi_00";

    public string WindowsBatteryName { get; set; } = "HID UPS";

    public string ListenPrefix { get; set; } = "http://127.0.0.1:9199/";

    public int PollIntervalSeconds { get; set; } = 5;

    public int HidTimeoutMilliseconds { get; set; } = 2_000;

    public int ReconnectDelaySeconds { get; set; } = 10;

    public int RawCaptureRetentionDays { get; set; }

    public string RawCaptureDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "VoltronicHidExporter",
        "capture");
}
