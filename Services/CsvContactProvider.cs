using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;

namespace socconvertor.Services;

public class CsvContactProvider : IContactProvider
{
    private readonly IConfiguration _config;
    private readonly Lazy<Dictionary<string, GoogleContactEntry>> _entries;

    public CsvContactProvider(IConfiguration config)
    {
        _config = config;
        _entries = new Lazy<Dictionary<string, GoogleContactEntry>>(Load, true);
    }

    public string? GetEmailForDebtor(string debtorCode)
    {
        var details = GetDetails(debtorCode);
        if (details == null) return null;
        if (details.To.Count > 0) return details.To[0];
        if (details.Cc.Count > 0) return details.Cc[0];
        if (details.Bcc.Count > 0) return details.Bcc[0];
        return null;
    }

    public GoogleContactEntry? GetDetails(string debtorCode)
    {
        if (string.IsNullOrWhiteSpace(debtorCode)) return null;
        var map = _entries.Value;
        return map.TryGetValue(debtorCode.Trim(), out var entry) ? entry : null;
    }

    private Dictionary<string, GoogleContactEntry> Load()
    {
        var result = new Dictionary<string, GoogleContactEntry>(StringComparer.OrdinalIgnoreCase);

        var path = _config["Contacts:Csv:Path"];
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return result; // no csv configured
        }

        // Prefer our robust Google parser
        try
        {
            var parsed = GoogleContactsCsvReader.Parse(path);
            foreach (var kv in parsed)
                result[kv.Key] = kv.Value;
            return result;
        }
        catch
        {
            // fallback below
        }

        // Fallback: simple two-column CSV (DebtorCode,Email)
        var debtorCodeCol = _config["Contacts:Csv:DebtorCodeColumn"] ?? "DebtorCode";
        var emailCol = _config["Contacts:Csv:EmailColumn"] ?? "Email";

        var conf = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            DetectDelimiter = true,
            PrepareHeaderForMatch = args => (args.Header ?? string.Empty).Trim(),
            MissingFieldFound = null,
            BadDataFound = null,
            HeaderValidated = null
        };

        using var reader = new StreamReader(path);
        using var csv = new CsvReader(reader, conf);
        try
        {
            var records = csv.GetRecords<dynamic>();
            foreach (IDictionary<string, object?> row in records)
            {
                var dict = row.ToDictionary(k => (k.Key ?? string.Empty).Trim(), v => v.Value?.ToString()?.Trim(), StringComparer.OrdinalIgnoreCase);
                var code = dict.ContainsKey(debtorCodeCol) ? dict[debtorCodeCol] : null;
                var email = dict.ContainsKey(emailCol) ? dict[emailCol] : null;
                if (!string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(email))
                {
                    result[code!] = new GoogleContactEntry
                    {
                        DebtorCode = code!,
                        To = new List<string> { email! }
                    };
                }
            }
        }
        catch { }

        return result;
    }
}
