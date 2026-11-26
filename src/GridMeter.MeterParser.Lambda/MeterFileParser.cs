using System.Globalization;

namespace GridMeter.MeterParser.Lambda;

public sealed record ParsedReading(string MeterId, DateTime Timestamp, decimal Kwh);

public sealed record ParseResult(IReadOnlyList<ParsedReading> Rows, int Rejected);

/// <summary>Parses `meter_id,timestamp,kwh` CSV files (30-minute intervals).</summary>
public static class MeterFileParser
{
    public const int BatchSize = 100;

    public static async Task<ParseResult> ParseAsync(TextReader reader)
    {
        var rows = new List<ParsedReading>();
        var rejected = 0;
        var header = await reader.ReadLineAsync();
        if (header is null) return new ParseResult(rows, 0);

        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split(',');
            if (parts.Length == 3
                && !string.IsNullOrWhiteSpace(parts[0])
                && DateTime.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var ts)
                && decimal.TryParse(parts[2], NumberStyles.Number, CultureInfo.InvariantCulture, out var kwh)
                && kwh >= 0)
            {
                rows.Add(new ParsedReading(parts[0].Trim(), ts, kwh));
            }
            else rejected++;
        }
        return new ParseResult(rows, rejected);
    }

    public static IEnumerable<IReadOnlyList<ParsedReading>> Batch(IReadOnlyList<ParsedReading> rows) =>
        rows.Chunk(BatchSize).Select(c => (IReadOnlyList<ParsedReading>)c);
}
