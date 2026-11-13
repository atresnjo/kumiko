using System.Globalization;
using System.Text;

namespace Kumiko.UI;

/// <summary>
/// CSV output. <paramref name="Separator"/> defaults to a comma. Cells starting with =, +, -, @, tab or carriage
/// return are prefixed with an apostrophe so spreadsheets show them as text, unless <paramref name="AllowFormulas"/>
/// is set; plain numbers such as "-5" are never changed.
/// </summary>
public readonly record struct CsvOptions(char Separator = ',', bool OmitHeader = false, bool AllowFormulas = false);

public sealed partial class DataGrid<T>
{
    /// <summary>
    /// Exports the filtered rows in displayed sort, group and column order as RFC 4180 CSV with CRLF line endings.
    /// </summary>
    public string ToCsv(CsvOptions options = default)
    {
        var writer = new StringWriter(CultureInfo.InvariantCulture);
        WriteCsv(writer, options);
        return writer.ToString();
    }

    /// <summary>Writes <see cref="ToCsv"/> output to <paramref name="writer"/>.</summary>
    public void WriteCsv(TextWriter writer, CsvOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(Delimited(ExportRows(), options.Separator == '\0' ? ',' : options.Separator, !options.OmitHeader,
            !options.AllowFormulas, "\r\n"));
    }

    string Delimited(IEnumerable<T> items, char separator, bool header, bool neutralize, string newline)
    {
        var displayed = Displayed.Select(i => columns[i]).ToArray();
        var text = new StringBuilder();
        void Line(IEnumerable<string> cells)
        {
            var first = true;
            foreach (var cell in cells)
            {
                if (!first)
                    text.Append(separator);
                first = false;
                Field(text, neutralize ? Neutralize(cell) : cell, separator);
            }
            text.Append(newline);
        }
        if (header)
            Line(displayed.Select(c => c.Header));
        foreach (var item in items)
            Line(displayed.Select(c => c.Value(item)));
        return text.ToString();
    }

    /// <summary>Quotes a field containing the separator, a quote or a line break, doubling inner quotes.</summary>
    static void Field(StringBuilder text, string value, char separator)
    {
        if (value.IndexOfAny([separator, '"', '\r', '\n']) < 0)
        {
            text.Append(value);
            return;
        }
        text.Append('"').Append(value.Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');
    }

    static string Neutralize(string value)
    {
        if (value.Length == 0 || value[0] is not ('=' or '+' or '-' or '@' or '\t' or '\r'))
            return value;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            return value;
        return "'" + value;
    }
}
