using System.Globalization;
using LifeLedger.Api.Contracts;

namespace LifeLedger.Api.Services;

/// <summary>Reads common bank and Revolut CSV exports in memory to estimate monthly outgoing spending.</summary>
public interface ICsvExpenseImportService
{
    /// <summary>Analyses CSV text without persisting individual transactions.</summary>
    CsvExpenseImportResponse Analyse(string csv);
}

/// <summary>Provides a dependency-free CSV analyser for private local expense estimates.</summary>
public sealed class CsvExpenseImportService : ICsvExpenseImportService
{
    /// <inheritdoc />
    public CsvExpenseImportResponse Analyse(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) throw new ArgumentException("The CSV file is empty.", nameof(csv));
        var rows = Parse(csv).ToArray();
        if (rows.Length < 2) throw new ArgumentException("The CSV file needs a header and at least one transaction.", nameof(csv));
        var headers = rows[0].Select(header => header.Trim().ToLowerInvariant()).ToArray();
        var amountColumn = Find(headers, "amount", "montant", "paid out", "debit") ?? throw new ArgumentException("No amount column was found. Use a column named Amount, Montant, Debit or Paid Out.", nameof(csv));
        var dateColumn = Find(headers, "completed date", "date", "booking date", "started date", "date opération");
        var descriptionColumn = Find(headers, "description", "merchant", "reference", "beneficiary");
        var currencyColumn = Find(headers, "currency", "devise");
        var expenses = new List<(DateOnly? Date, decimal Amount, string Description, string Currency)>();
        foreach (var row in rows.Skip(1))
        {
            if (amountColumn >= row.Length || !TryAmount(row[amountColumn], out var amount) || amount >= 0m) continue;
            var description = descriptionColumn is { } descriptionIndex && descriptionIndex < row.Length ? row[descriptionIndex] : string.Empty;
            var transactionCurrency = currencyColumn is { } currencyIndex && currencyIndex < row.Length ? row[currencyIndex].Trim().ToUpperInvariant() : "EUR";
            var date = dateColumn is { } dateIndex && dateIndex < row.Length ? ParseDate(row[dateIndex]) : null;
            expenses.Add((date, Math.Abs(amount), description, transactionCurrency.Length == 3 ? transactionCurrency : "EUR"));
        }
        var months = Math.Max(1, expenses.Where(item => item.Date is not null).Select(item => $"{item.Date!.Value.Year:D4}-{item.Date!.Value.Month:D2}").Distinct().Count());
        var currency = expenses.GroupBy(item => item.Currency).OrderByDescending(group => group.Count()).FirstOrDefault()?.Key ?? "EUR";
        var total = expenses.Where(item => item.Currency == currency).Sum(item => item.Amount);
        var categories = expenses.Where(item => item.Currency == currency).GroupBy(item => Category(item.Description)).Select(group => new CsvExpenseCategory(group.Key, Math.Round(group.Sum(item => item.Amount), 2))).OrderByDescending(item => item.Total).Take(8).ToArray();
        return new CsvExpenseImportResponse(expenses.Count, months, Math.Round(total, 2), Math.Round(total / months, 2), currency, categories);
    }

    /// <summary>Finds a matching normalised header in priority order.</summary>
    private static int? Find(IReadOnlyList<string> headers, params string[] names) { foreach (var name in names) { for (var index = 0; index < headers.Count; index++) if (headers[index] == name) return index; } return null; }
    /// <summary>Parses a signed bank amount using common European and English decimal formats.</summary>
    private static bool TryAmount(string value, out decimal amount) => decimal.TryParse(value.Replace(" ", string.Empty), NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out amount) || decimal.TryParse(value.Replace(" ", string.Empty), NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.GetCultureInfo("fr-FR"), out amount);
    /// <summary>Parses ISO and local date formats used by typical statement exports.</summary>
    private static DateOnly? ParseDate(string value) => DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) || DateOnly.TryParse(value, CultureInfo.GetCultureInfo("fr-FR"), DateTimeStyles.None, out date) ? date : null;
    /// <summary>Uses simple local keywords so the user can understand every inferred category.</summary>
    private static string Category(string description) { var text = description.ToLowerInvariant(); return text.Contains("restaurant") || text.Contains("cafe") || text.Contains("bar") ? "Restaurants & sorties" : text.Contains("market") || text.Contains("super") || text.Contains("carrefour") ? "Courses" : text.Contains("uber") || text.Contains("bolt") || text.Contains("train") || text.Contains("fuel") ? "Transport" : text.Contains("hotel") || text.Contains("airbnb") || text.Contains("booking") ? "Vacances" : "Autres dépenses"; }
    /// <summary>Parses quoted CSV rows while accepting comma or semicolon delimiters.</summary>
    private static IEnumerable<string[]> Parse(string csv) { var delimiter = csv.Contains(';') && !csv.Contains(',') ? ';' : ','; foreach (var line in csv.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)) { var fields = new List<string>(); var current = new System.Text.StringBuilder(); var quoted = false; foreach (var character in line) { if (character == '"') quoted = !quoted; else if (character == delimiter && !quoted) { fields.Add(current.ToString()); current.Clear(); } else current.Append(character); } fields.Add(current.ToString()); yield return fields.ToArray(); } }
}
