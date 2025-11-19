using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Configuration;

namespace PdfReaderDemo.Services;

public class CsvContactProvider : IContactProvider
{
    private readonly IConfiguration _config;
    private readonly Lazy<Dictionary<string, string>> _entries;

    public CsvContactProvider(IConfiguration config)
    {
        _config = config;
        _entries = new Lazy<Dictionary<string, string>>(Load, true);
    }

    public string? GetEmailForDebtor(string debtorCode)
    {
        if (string.IsNullOrWhiteSpace(debtorCode)) return null;
        var map = _entries.Value;
        return map.TryGetValue(debtorCode, out var email) ? email : null;
    }

    private Dictionary<string, string> Load()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var path = _config["Contacts:Csv:Path"];
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return result; // no csv configured
        }

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

                // Support Google Contacts export: default email column is "E-mail 1 - Value"
                var email = GetFirstNonEmpty(dict, emailCol, "E-mail 1 - Value", "Email 1 - Value");

                // Debtor code: prefer configured column. For Google export, allow custom field named "Custom Field 1 - Value" to carry debtor code
                var code = GetFirstNonEmpty(dict, debtorCodeCol, "Custom Field 1 - Value", "User Defined 1 - Value");

                if (!string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(email))
                {
                    result[code] = email;
                }
            }
        }
        catch
        {
            // swallow parse errors; provider will just have fewer entries
        }

        return result;
    }

    private static string? GetFirstNonEmpty(IDictionary<string, string?> dict, params string[] keys)
    {
        foreach (var k in keys)
        {
            if (!string.IsNullOrWhiteSpace(k) && dict.TryGetValue(k, out var val))
            {
                var trimmed = val?.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed)) return trimmed;
            }
        }
        return null;
    }
}
