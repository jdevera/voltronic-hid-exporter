using System.Management;

namespace VoltronicHidExporter;

public sealed class WindowsBatteryReader(ExporterOptions options)
{
    public WindowsBatterySnapshot Read()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new WindowsBatterySnapshot(null, null, null, null, "WMI is available only on Windows.");
        }

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, Status, BatteryStatus, EstimatedChargeRemaining, EstimatedRunTime " +
                "FROM Win32_Battery");
            using var results = searcher.Get();

            foreach (ManagementObject battery in results)
            {
                using (battery)
                {
                    var name = battery["Name"]?.ToString();
                    if (!string.Equals(name, options.WindowsBatteryName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    return new WindowsBatterySnapshot(
                        battery["Status"]?.ToString(),
                        ConvertNullable<ushort>(battery["BatteryStatus"]),
                        ConvertNullable<ushort>(battery["EstimatedChargeRemaining"]),
                        ConvertNullable<ulong>(battery["EstimatedRunTime"]),
                        null);
                }
            }

            return new WindowsBatterySnapshot(
                null,
                null,
                null,
                null,
                $"Win32_Battery did not contain '{options.WindowsBatteryName}'.");
        }
        catch (Exception exception)
        {
            return new WindowsBatterySnapshot(null, null, null, null, exception.Message);
        }
    }

    private static T? ConvertNullable<T>(object? value)
        where T : struct, IConvertible
    {
        if (value is null)
        {
            return null;
        }

        return (T)Convert.ChangeType(value, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }
}
