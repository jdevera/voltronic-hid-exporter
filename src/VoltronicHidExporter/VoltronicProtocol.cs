using System.Globalization;

namespace VoltronicHidExporter;

public static class VoltronicProtocol
{
    private static readonly CultureInfo ProtocolCulture = CultureInfo.InvariantCulture;

    public static string ParseDialect(string response)
    {
        var dialect = Clean(response);
        if (dialect is not ("H" or "V"))
        {
            throw new FormatException($"Unsupported protocol dialect response '{dialect}'.");
        }

        return dialect;
    }

    public static UpsRatings ParseRatings(string response)
    {
        var clean = Clean(response);
        if (!clean.StartsWith('#'))
        {
            throw new FormatException("Ratings response must begin with '#'.");
        }

        var fields = clean[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 4)
        {
            throw new FormatException($"Ratings response has {fields.Length} fields; expected 4.");
        }

        return new UpsRatings(
            ParseNumber(fields[0], "nominal output voltage"),
            ParseNumber(fields[1], "nominal output current"),
            ParseNumber(fields[2], "nominal battery voltage"),
            ParseNumber(fields[3], "nominal frequency"));
    }

    public static UpsTelemetry ParseQueryStatus(string response)
    {
        var clean = Clean(response);
        if (!clean.StartsWith('('))
        {
            throw new FormatException("Status response must begin with '('.");
        }

        var fields = clean[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 8)
        {
            throw new FormatException($"Status response has {fields.Length} fields; expected 8.");
        }

        var status = fields[7];
        if (status.Length != 8 || status.Any(bit => bit is not ('0' or '1')))
        {
            throw new FormatException("Status field must contain exactly eight binary digits.");
        }

        double? temperature = fields[6] == "--.-"
            ? null
            : ParseNumber(fields[6], "temperature");

        return new UpsTelemetry(
            ParseNumber(fields[0], "input voltage"),
            ParseNumber(fields[1], "input-fault voltage"),
            ParseNumber(fields[2], "output voltage"),
            ParseNumber(fields[3], "load percentage"),
            ParseNumber(fields[4], "frequency"),
            ParseNumber(fields[5], "battery voltage"),
            temperature,
            status,
            status[0] == '1',
            status[1] == '1',
            status[2] == '1',
            status[3] == '1',
            status[4] == '1',
            status[5] == '1',
            status[6] == '1',
            status[7] == '1');
    }

    private static string Clean(string response) => response.Trim('\0', '\r', '\n', ' ');

    private static double ParseNumber(string value, string fieldName)
    {
        if (!double.TryParse(value, NumberStyles.Float, ProtocolCulture, out var parsed))
        {
            throw new FormatException($"Invalid {fieldName} value '{value}'.");
        }

        return parsed;
    }
}
