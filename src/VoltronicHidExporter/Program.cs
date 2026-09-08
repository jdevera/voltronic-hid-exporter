using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace VoltronicHidExporter;

public static class Program
{
    private const string ServiceName = "VoltronicHidExporter";

    public static async Task<int> Main(string[] args)
    {
        var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "run";
        if (args.Length > 1 || command is not ("run" or "probe" or "version" or "print-default-config" or "help" or "--help" or "-h"))
        {
            Console.Error.WriteLine("Unknown command. Run 'voltronic-hid-exporter help' for usage.");
            return 2;
        }

        if (command is "help" or "--help" or "-h")
        {
            PrintHelp();
            return 0;
        }

        if (command == "version")
        {
            Console.WriteLine(Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown");
            return 0;
        }

        if (command == "print-default-config")
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new Dictionary<string, ExporterOptions>
                {
                    [ExporterOptions.SectionName] = new ExporterOptions(),
                },
                new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        var configuration = BuildConfiguration();
        var options = configuration
            .GetSection(ExporterOptions.SectionName)
            .Get<ExporterOptions>() ?? new ExporterOptions();

        if (command == "probe")
        {
            return RunProbe(options);
        }

        var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
        builder.Configuration.AddConfiguration(configuration);
        builder.Services.AddWindowsService(service => service.ServiceName = ServiceName);
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<ExporterState>();
        builder.Services.AddSingleton<HidUpsClient>();
        builder.Services.AddSingleton<WindowsBatteryReader>();
        builder.Services.AddSingleton<RawCaptureWriter>();
        builder.Services.AddHostedService<UpsPollingService>();
        builder.Services.AddHostedService<MetricsServer>();

        await builder.Build().RunAsync();
        return 0;
    }

    private static IConfigurationRoot BuildConfiguration()
    {
        var programDataConfig = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "VoltronicHidExporter",
            "appsettings.json");

        return new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile(programDataConfig, optional: true, reloadOnChange: false)
            .AddEnvironmentVariables(prefix: "VOLTRONIC_HID_EXPORTER_")
            .Build();
    }

    private static int RunProbe(ExporterOptions options)
    {
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddSimpleConsole());
        try
        {
            var client = new HidUpsClient(options);
            using var session = client.Open();
            var protocol = VoltronicProtocol.ParseDialect(session.Query("M"));
            var ratings = VoltronicProtocol.ParseRatings(session.Query("F"));
            var rawQueryStatus = session.Query("QS");
            var snapshot = new PollSnapshot(
                DateTimeOffset.UtcNow,
                session.DevicePath,
                protocol,
                ratings,
                rawQueryStatus,
                VoltronicProtocol.ParseQueryStatus(rawQueryStatus),
                new WindowsBatteryReader(options).Read());

            Console.WriteLine(JsonSerializer.Serialize(
                snapshot,
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true,
                }));
            return 0;
        }
        catch (Exception exception)
        {
            loggerFactory.CreateLogger("probe").LogError(exception, "UPS probe failed");
            return 1;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            Voltronic HID Exporter

            Usage:
              voltronic-hid-exporter [run]
              voltronic-hid-exporter probe
              voltronic-hid-exporter version
              voltronic-hid-exporter print-default-config
              voltronic-hid-exporter help

            The exporter implements only the read-only M, F, and QS protocol queries.
            Configuration is read from %ProgramData%\VoltronicHidExporter\appsettings.json.
            """);
    }
}
