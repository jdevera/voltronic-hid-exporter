using System.Text;
using HidSharp;

namespace VoltronicHidExporter;

public sealed class HidUpsClient(ExporterOptions options)
{
    private static readonly HashSet<string> AllowedCommands = new(StringComparer.Ordinal)
    {
        "M",
        "F",
        "QS",
    };

    public HidUpsSession Open()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("USB HID polling is supported only on Windows.");
        }

        var devices = DeviceList.Local
            .GetHidDevices(options.VendorId, options.ProductId)
            .ToArray();

        var device = devices.FirstOrDefault(candidate =>
            candidate.DevicePath.Contains(options.InterfaceToken, StringComparison.OrdinalIgnoreCase));

        if (device is null)
        {
            var paths = devices.Length == 0
                ? "no matching VID/PID devices found"
                : string.Join(", ", devices.Select(candidate => candidate.DevicePath));
            throw new IOException(
                $"Could not find HID interface '{options.InterfaceToken}' for " +
                $"{options.VendorId:X4}:{options.ProductId:X4}; discovered: {paths}.");
        }

        if (!device.TryOpen(out var stream))
        {
            throw new IOException($"Could not open HID device '{device.DevicePath}'.");
        }

        stream.ReadTimeout = options.HidTimeoutMilliseconds;
        stream.WriteTimeout = options.HidTimeoutMilliseconds;
        return new HidUpsSession(device, stream);
    }

    public sealed class HidUpsSession(HidDevice device, HidStream stream) : IDisposable
    {
        public string DevicePath => device.DevicePath;

        public string Query(string command)
        {
            if (!AllowedCommands.Contains(command))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(command),
                    command,
                    "Only the read-only M, F, and QS commands are implemented.");
            }

            var commandBytes = Encoding.ASCII.GetBytes(command + "\r");
            var outputLength = device.GetMaxOutputReportLength();
            if (outputLength < commandBytes.Length + 1)
            {
                throw new IOException(
                    $"HID output report length {outputLength} is too small for '{command}'.");
            }

            var output = new byte[outputLength];
            Buffer.BlockCopy(commandBytes, 0, output, 1, commandBytes.Length);
            stream.Write(output);

            var input = new byte[device.GetMaxInputReportLength()];
            var response = new StringBuilder();

            while (response.Length < 4_096)
            {
                var bytesRead = stream.Read(input, 0, input.Length);
                if (bytesRead <= 0)
                {
                    throw new IOException($"UPS returned no data for '{command}'.");
                }

                // HidSharp includes the report-ID byte on Windows. Be tolerant of
                // transports which omit it, but never include a zero report ID in
                // the textual Voltronic response.
                var start = input[0] == 0 ? 1 : 0;
                for (var index = start; index < bytesRead; index++)
                {
                    var value = input[index];
                    if (value == '\r')
                    {
                        return response.ToString();
                    }

                    if (value != 0)
                    {
                        response.Append((char)value);
                    }
                }
            }

            throw new IOException($"UPS response to '{command}' exceeded 4096 bytes.");
        }

        public void Dispose() => stream.Dispose();
    }
}
