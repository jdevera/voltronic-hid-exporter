using System.Globalization;
using System.Reflection;
using System.Text;

namespace VoltronicHidExporter;

public static class PrometheusRenderer
{
    private static readonly CultureInfo MetricsCulture = CultureInfo.InvariantCulture;

    public static string Render(ExporterStateView state, DateTimeOffset now, ExporterOptions options)
    {
        var output = new StringBuilder();

        Gauge(output, "voltronic_hid_exporter_up", "Whether the most recent UPS poll succeeded.", state.Up);
        Counter(output, "voltronic_hid_exporter_poll_errors_total", "Total failed UPS polling attempts.", state.PollErrors);
        Counter(output, "voltronic_hid_utility_losses_total", "Total observed transitions to battery power.", state.UtilityLosses);

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        output.AppendLine("# HELP voltronic_hid_exporter_build_info Exporter build information.");
        output.AppendLine("# TYPE voltronic_hid_exporter_build_info gauge");
        output.Append("voltronic_hid_exporter_build_info{version=\"")
            .Append(EscapeLabel(version))
            .AppendLine("\"} 1");

        if (state.LastUtilityLoss is not null)
        {
            Gauge(
                output,
                "voltronic_hid_last_utility_loss_timestamp_seconds",
                "Unix timestamp of the most recently observed transition to battery power.",
                state.LastUtilityLoss.Value.ToUnixTimeSeconds());
        }

        Gauge(
            output,
            "voltronic_hid_current_outage_duration_seconds",
            "Duration of the current utility outage, or zero while utility power is present.",
            state.CurrentOutageStarted is null
                ? 0
                : Math.Max(0, (now - state.CurrentOutageStarted.Value).TotalSeconds));

        var snapshot = state.LastSnapshot;
        if (snapshot is null)
        {
            return output.ToString();
        }

        Gauge(
            output,
            "voltronic_hid_exporter_last_success_timestamp_seconds",
            "Unix timestamp of the most recent successful UPS poll.",
            snapshot.Timestamp.ToUnixTimeSeconds());

        output.AppendLine("# HELP voltronic_hid_ups_info Static UPS identity information.");
        output.AppendLine("# TYPE voltronic_hid_ups_info gauge");
        output.Append("voltronic_hid_ups_info{protocol=\"")
            .Append(EscapeLabel(snapshot.Protocol))
            .Append("\",vendor_id=\"")
            .Append(options.VendorId.ToString("X4", MetricsCulture))
            .Append("\",product_id=\"")
            .Append(options.ProductId.ToString("X4", MetricsCulture))
            .AppendLine("\"} 1");

        Gauge(output, "voltronic_hid_input_voltage_volts", "UPS input voltage.", snapshot.Telemetry.InputVoltage);
        Gauge(output, "voltronic_hid_input_fault_voltage_volts", "UPS input-fault voltage retained by the device.", snapshot.Telemetry.InputFaultVoltage);
        Gauge(output, "voltronic_hid_output_voltage_volts", "UPS output voltage.", snapshot.Telemetry.OutputVoltage);
        Gauge(output, "voltronic_hid_load_percent", "UPS output load percentage.", snapshot.Telemetry.LoadPercent);
        Gauge(output, "voltronic_hid_frequency_hertz", "UPS output frequency.", snapshot.Telemetry.Frequency);
        Gauge(output, "voltronic_hid_battery_voltage_volts", "UPS battery voltage.", snapshot.Telemetry.BatteryVoltage);
        if (snapshot.Telemetry.Temperature is not null)
        {
            Gauge(output, "voltronic_hid_temperature_celsius", "UPS temperature.", snapshot.Telemetry.Temperature.Value);
        }

        Gauge(output, "voltronic_hid_on_battery", "Whether utility power has failed and the UPS is on battery.", snapshot.Telemetry.OnBattery);
        Gauge(output, "voltronic_hid_battery_low", "Whether the UPS reports a low battery.", snapshot.Telemetry.BatteryLow);
        Gauge(output, "voltronic_hid_boost_or_buck_active", "Whether automatic voltage regulation is active.", snapshot.Telemetry.BoostOrBuck);
        Gauge(output, "voltronic_hid_fault", "Whether the UPS reports a fault.", snapshot.Telemetry.Fault);
        Gauge(output, "voltronic_hid_line_interactive", "Whether the UPS identifies itself as line-interactive.", snapshot.Telemetry.LineInteractive);
        Gauge(output, "voltronic_hid_self_test", "Whether the UPS is performing a self-test.", snapshot.Telemetry.SelfTest);
        Gauge(output, "voltronic_hid_shutdown_pending", "Whether the UPS reports a pending shutdown.", snapshot.Telemetry.ShutdownPending);
        Gauge(output, "voltronic_hid_beeper_enabled", "Whether the UPS beeper is enabled.", snapshot.Telemetry.BeeperEnabled);

        Gauge(output, "voltronic_hid_rated_output_voltage_volts", "Nominal UPS output voltage.", snapshot.Ratings.NominalOutputVoltage);
        Gauge(output, "voltronic_hid_rated_output_current_amperes", "Nominal output current reported by the UPS.", snapshot.Ratings.NominalOutputCurrent);
        Gauge(output, "voltronic_hid_rated_battery_voltage_volts", "Nominal UPS battery voltage.", snapshot.Ratings.NominalBatteryVoltage);
        Gauge(output, "voltronic_hid_rated_frequency_hertz", "Nominal UPS frequency.", snapshot.Ratings.NominalFrequency);

        Gauge(
            output,
            "voltronic_hid_windows_battery_up",
            "Whether Windows returned its native HID battery data.",
            snapshot.WindowsBattery.Error is null);
        OptionalGauge(output, "voltronic_hid_windows_battery_status", "Windows Win32_Battery status code.", snapshot.WindowsBattery.BatteryStatus);
        OptionalGauge(output, "voltronic_hid_windows_battery_charge_percent", "Windows native HID battery charge percentage.", snapshot.WindowsBattery.ChargePercent);
        if (snapshot.WindowsBattery.EstimatedRuntimeMinutes is not null)
        {
            Gauge(
                output,
                "voltronic_hid_windows_battery_runtime_seconds",
                "Windows native HID battery estimated runtime in seconds.",
                snapshot.WindowsBattery.EstimatedRuntimeMinutes.Value * 60d);
        }

        return output.ToString();
    }

    private static void OptionalGauge<T>(StringBuilder output, string name, string help, T? value)
        where T : struct, IConvertible
    {
        if (value is not null)
        {
            Gauge(output, name, help, Convert.ToDouble(value.Value, MetricsCulture));
        }
    }

    private static void Gauge(StringBuilder output, string name, string help, bool value) =>
        Gauge(output, name, help, value ? 1 : 0);

    private static void Gauge(StringBuilder output, string name, string help, double value)
    {
        output.Append("# HELP ").Append(name).Append(' ').AppendLine(help);
        output.Append("# TYPE ").Append(name).AppendLine(" gauge");
        output.Append(name).Append(' ').AppendLine(value.ToString("G17", MetricsCulture));
    }

    private static void Counter(StringBuilder output, string name, string help, long value)
    {
        output.Append("# HELP ").Append(name).Append(' ').AppendLine(help);
        output.Append("# TYPE ").Append(name).AppendLine(" counter");
        output.Append(name).Append(' ').AppendLine(value.ToString(MetricsCulture));
    }

    private static string EscapeLabel(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal);
}
