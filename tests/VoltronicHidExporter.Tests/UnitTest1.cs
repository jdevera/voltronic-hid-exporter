namespace VoltronicHidExporter.Tests;

public sealed class VoltronicProtocolTests
{
    [Fact]
    public void ParsesObservedSalicruDialect()
    {
        Assert.Equal("H", VoltronicProtocol.ParseDialect("H\r"));
    }

    [Fact]
    public void ParsesObservedRatings()
    {
        var ratings = VoltronicProtocol.ParseRatings("#230.0 002 12.00 50.0\r");

        Assert.Equal(230.0, ratings.NominalOutputVoltage);
        Assert.Equal(2.0, ratings.NominalOutputCurrent);
        Assert.Equal(12.0, ratings.NominalBatteryVoltage);
        Assert.Equal(50.0, ratings.NominalFrequency);
    }

    [Fact]
    public void ParsesObservedOnlineStatus()
    {
        var telemetry = VoltronicProtocol.ParseQueryStatus(
            "(226.5 226.5 226.5 009 50.1 13.6 --.- 00001001\r");

        Assert.Equal(226.5, telemetry.InputVoltage);
        Assert.Equal(9.0, telemetry.LoadPercent);
        Assert.Equal(13.6, telemetry.BatteryVoltage);
        Assert.Null(telemetry.Temperature);
        Assert.False(telemetry.OnBattery);
        Assert.False(telemetry.BatteryLow);
        Assert.True(telemetry.LineInteractive);
        Assert.True(telemetry.BeeperEnabled);
    }

    [Fact]
    public void ParsesEveryStatusBitInDocumentedOrder()
    {
        var telemetry = VoltronicProtocol.ParseQueryStatus(
            "(000.0 000.0 230.0 020 50.0 11.8 30.0 11111111");

        Assert.True(telemetry.OnBattery);
        Assert.True(telemetry.BatteryLow);
        Assert.True(telemetry.BoostOrBuck);
        Assert.True(telemetry.Fault);
        Assert.True(telemetry.LineInteractive);
        Assert.True(telemetry.SelfTest);
        Assert.True(telemetry.ShutdownPending);
        Assert.True(telemetry.BeeperEnabled);
    }

    [Theory]
    [InlineData("")]
    [InlineData("(226.5 226.5")]
    [InlineData("(226.5 226.5 226.5 009 50.1 13.6 --.- 0000100X")]
    public void RejectsMalformedStatus(string response)
    {
        Assert.Throws<FormatException>(() => VoltronicProtocol.ParseQueryStatus(response));
    }
}

public sealed class ExporterStateTests
{
    [Fact]
    public void CountsPowerCutsAndTracksRestoration()
    {
        var state = new ExporterState();
        var start = DateTimeOffset.Parse("2026-09-08T01:00:00Z");

        Assert.Equal(UtilityTransition.None, state.RecordSuccess(Snapshot(start, onBattery: false)));
        Assert.Equal(UtilityTransition.UtilityLost, state.RecordSuccess(Snapshot(start.AddSeconds(5), onBattery: true)));
        Assert.Equal(UtilityTransition.None, state.RecordSuccess(Snapshot(start.AddSeconds(10), onBattery: true)));
        Assert.Equal(UtilityTransition.UtilityRestored, state.RecordSuccess(Snapshot(start.AddSeconds(15), onBattery: false)));

        var view = state.Read();
        Assert.Equal(1, view.UtilityLosses);
        Assert.Equal(start.AddSeconds(5), view.LastUtilityLoss);
        Assert.Null(view.CurrentOutageStarted);
    }

    [Fact]
    public void AFailureKeepsTheLastGoodMeasurementsButMarksExporterDown()
    {
        var state = new ExporterState();
        var snapshot = Snapshot(DateTimeOffset.UtcNow, onBattery: false);
        state.RecordSuccess(snapshot);

        state.RecordFailure("USB disconnected");

        var view = state.Read();
        Assert.False(view.Up);
        Assert.Same(snapshot, view.LastSnapshot);
        Assert.Equal(1, view.PollErrors);
    }

    private static PollSnapshot Snapshot(DateTimeOffset timestamp, bool onBattery) => new(
        timestamp,
        "device",
        "H",
        new UpsRatings(230, 2, 12, 50),
        "raw",
        new UpsTelemetry(230, 230, 230, 10, 50, 13.6, null, "00001001", onBattery, false, false, false, true, false, false, true),
        new WindowsBatterySnapshot("OK", 2, 100, 91, null));
}

public sealed class PrometheusRendererTests
{
    [Fact]
    public void RendersObservedMeasurementsAndConfiguredUsbIdentity()
    {
        var state = new ExporterState();
        var timestamp = DateTimeOffset.Parse("2026-09-08T01:00:00Z");
        state.RecordSuccess(new PollSnapshot(
            timestamp,
            "device",
            "H",
            new UpsRatings(230, 2, 12, 50),
            "raw",
            new UpsTelemetry(226.5, 226.5, 226.5, 9, 50.1, 13.6, null, "00001001", false, false, false, false, true, false, false, true),
            new WindowsBatterySnapshot("OK", 2, 97, 91, null)));

        var metrics = PrometheusRenderer.Render(
            state.Read(),
            timestamp.AddMinutes(1),
            new ExporterOptions { VendorId = 0x1234, ProductId = 0xABCD });

        Assert.Contains("voltronic_hid_input_voltage_volts 226.5", metrics);
        Assert.Contains("voltronic_hid_windows_battery_charge_percent 97", metrics);
        Assert.Contains("vendor_id=\"1234\",product_id=\"ABCD\"", metrics);
    }
}

public sealed class ServiceSnapshotClientTests
{
    [Theory]
    [InlineData("http://127.0.0.1:9199/", "http://127.0.0.1:9199/snapshot")]
    [InlineData("http://+:9199/", "http://127.0.0.1:9199/snapshot")]
    [InlineData("http://*:9199/", "http://127.0.0.1:9199/snapshot")]
    public void BuildsALoopbackSnapshotUri(string listenPrefix, string expected)
    {
        Assert.Equal(expected, ServiceSnapshotClient.BuildLoopbackUri(listenPrefix, "/snapshot").ToString());
    }
}
