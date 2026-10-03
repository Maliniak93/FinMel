using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ExcelDataReader;
using Microsoft.Extensions.Logging.Abstractions;
using Skarbiec.Contracts;

namespace Skarbiec.MarketData.Sources.MfBonds;

// Columns are found by header text, never by position: the sheets differ in width and MF reshapes them.
public static partial class MfBondFileParser
{
    private const string SeriesHeader = "Seria";
    private const string IsinHeader = "Kod ISIN";
    private const string SaleStartHeader = "Początek sprzedaży";
    private const string SaleEndHeader = "Koniec sprzedaży";
    private const string IssuePriceHeader = "Cena emisyjna";
    private const string SwapPriceHeader = "Cena zamiany";
    private const string RateHeader = "Oprocentowanie";
    private const string MarginHeader = "Marża";

    private static readonly Dictionary<string, TreasuryBondType> SheetTypes =
        Enum.GetValues<TreasuryBondType>().ToDictionary(t => t.ToString().ToUpperInvariant(), StringComparer.OrdinalIgnoreCase);

    // InvariantGlobalization rules out pl-PL, so its number and date shapes are spelled out.
    private static readonly NumberFormatInfo PolishNumbers = new() { NumberDecimalSeparator = ",", NumberGroupSeparator = " " };

    private static readonly string[] DateFormats = ["d.M.yyyy", "yyyy-MM-dd"];

    // The file is code page 1250, which .NET knows only once this provider is registered; registering it again is a no-op.
    static MfBondFileParser() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static IReadOnlyList<ParsedBondSeries> Parse(Stream stream, ILogger? logger = null)
    {
        logger ??= NullLogger.Instance;
        var series = new List<ParsedBondSeries>();

        using var reader = ExcelReaderFactory.CreateReader(stream);
        do
        {
            if (SheetTypes.TryGetValue(reader.Name, out var type))
            {
                series.AddRange(ParseSheet(reader, type, logger));
            }
        }
        while (reader.NextResult());

        return series;
    }

    private static List<ParsedBondSeries> ParseSheet(IExcelDataReader reader, TreasuryBondType type, ILogger logger)
    {
        var rows = ReadRows(reader);
        if (rows.Count == 0)
        {
            throw new InvalidDataException($"Sheet '{reader.Name}' is empty.");
        }

        var header = rows[0].Select(Text).ToArray();
        var (rates, firstDataRow) = RateColumns(header, rows, reader.Name);
        var columns = new SheetColumns(
            Series: RequiredColumn(header, SeriesHeader, reader.Name),
            Isin: RequiredColumn(header, IsinHeader, reader.Name),
            SaleStart: RequiredColumn(header, SaleStartHeader, reader.Name),
            SaleEnd: RequiredColumn(header, SaleEndHeader, reader.Name),
            IssuePrice: RequiredColumn(header, IssuePriceHeader, reader.Name),
            SwapPrice: RequiredColumn(header, SwapPriceHeader, reader.Name),
            Margin: Column(header, MarginHeader),
            Rates: rates);

        var series = new List<ParsedBondSeries>();
        foreach (var row in rows.Skip(firstDataRow))
        {
            var code = Text(Cell(row, columns.Series));
            if (code is null || !SeriesCode().IsMatch(code))
            {
                continue;
            }

            if (!code.StartsWith(type.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("MF bond file: series {Code} on sheet {Sheet} does not match the sheet's type; skipped.", code, reader.Name);
                continue;
            }

            series.Add(ParseRow(row, code, type, columns));
        }

        return series;
    }

    private static List<object?[]> ReadRows(IExcelDataReader reader)
    {
        var rows = new List<object?[]>();
        while (reader.Read())
        {
            var row = new object?[reader.FieldCount];
            for (var i = 0; i < row.Length; i++)
            {
                row[i] = reader.GetValue(i);
            }

            rows.Add(row);
        }

        return rows;
    }

    // A one-column block (OTS, TOS) has data from row 2; a wider one has "w N. okresie" / "w N. roku" sub-headers on row 2.
    private static (int[] Columns, int FirstDataRow) RateColumns(string?[] header, List<object?[]> rows, string sheet)
    {
        var start = RequiredColumn(header, RateHeader, sheet);
        var end = start + 1;
        while (end < header.Length && header[end] is null)
        {
            end++;
        }

        if (end - start == 1)
        {
            return ([start], 1);
        }

        var subHeader = rows.Count > 1 ? rows[1] : [];
        var periods = new List<(int Period, int Column)>();
        for (var column = start; column < end; column++)
        {
            var match = PeriodHeader().Match(Text(Cell(subHeader, column)) ?? string.Empty);
            if (!match.Success)
            {
                throw new InvalidDataException($"Sheet '{sheet}': column {column} of '{RateHeader}' has no period header.");
            }

            periods.Add((int.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture), column));
        }

        return (periods.OrderBy(p => p.Period).Select(p => p.Column).ToArray(), 2);
    }

    // Rates are published period by period, so the first empty cell ends the series' known rates.
    private static ParsedBondSeries ParseRow(object?[] row, string code, TreasuryBondType type, SheetColumns columns)
    {
        var rates = new List<decimal>();
        foreach (var column in columns.Rates)
        {
            var rate = Number(Cell(row, column), code);
            if (rate is null)
            {
                break;
            }

            rates.Add(Percent(rate.Value));
        }

        var margin = columns.Margin is { } marginColumn ? Number(Cell(row, marginColumn), code) : null;

        return new ParsedBondSeries(
            code,
            type,
            Text(Cell(row, columns.Isin)) ?? throw Missing(code, IsinHeader),
            Date(Cell(row, columns.SaleStart), code) ?? throw Missing(code, SaleStartHeader),
            Date(Cell(row, columns.SaleEnd), code) ?? throw Missing(code, SaleEndHeader),
            Price(Number(Cell(row, columns.IssuePrice), code)) ?? throw Missing(code, IssuePriceHeader),
            Price(Number(Cell(row, columns.SwapPrice), code)),
            margin is null ? null : Percent(margin.Value),
            rates);
    }

    private static int RequiredColumn(string?[] header, string name, string sheet) =>
        Column(header, name) ?? throw new InvalidDataException($"Sheet '{sheet}' has no '{name}' column.");

    private static int? Column(string?[] header, string name)
    {
        var index = Array.FindIndex(header, h => string.Equals(h, name, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? null : index;
    }

    private static object? Cell(object?[] row, int column) => column < row.Length ? row[column] : null;

    private static string? Text(object? value)
    {
        var text = value switch
        {
            null => null,
            string s => s,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };

        return string.IsNullOrWhiteSpace(text) ? null : Whitespace().Replace(text, " ").Trim();
    }

    private static bool IsBlank(string? text) => text is null or "-" or "–" or "—";

    private static decimal? Number(object? value, string code)
    {
        switch (value)
        {
            case double number:
                return (decimal)number;
            case decimal number:
                return number;
            case int number:
                return number;
        }

        var text = Text(value);
        if (IsBlank(text))
        {
            return null;
        }

        if (decimal.TryParse(text, NumberStyles.Number, PolishNumbers, out var parsed)
            || decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed))
        {
            return parsed;
        }

        throw new InvalidDataException($"Series {code}: '{text}' is not a number.");
    }

    // The reader turns date-formatted cells into DateTime; an unformatted one arrives as the raw Excel serial number.
    private static DateOnly? Date(object? value, string code)
    {
        switch (value)
        {
            case DateTime dateTime:
                return DateOnly.FromDateTime(dateTime);
            case double serial:
                return DateOnly.FromDateTime(DateTime.FromOADate(serial));
        }

        var text = Text(value);
        if (IsBlank(text))
        {
            return null;
        }

        if (DateOnly.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return parsed;
        }

        throw new InvalidDataException($"Series {code}: '{text}' is not a date.");
    }

    private static decimal Percent(decimal fraction) => Math.Round(fraction * 100m, 2, MidpointRounding.AwayFromZero);

    private static decimal? Price(decimal? value) => value is null ? null : Math.Round(value.Value, 2, MidpointRounding.AwayFromZero);

    private static InvalidDataException Missing(string code, string column) => new($"Series {code} has no '{column}'.");

    private sealed record SheetColumns(
        int Series, int Isin, int SaleStart, int SaleEnd, int IssuePrice, int SwapPrice, int? Margin, int[] Rates);

    [GeneratedRegex(@"^[A-Z]{3}\d{4}$")]
    private static partial Regex SeriesCode();

    [GeneratedRegex(@"^w (?<n>\d+)\. (okresie|roku)$", RegexOptions.IgnoreCase)]
    private static partial Regex PeriodHeader();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
