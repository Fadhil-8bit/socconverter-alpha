using Microsoft.VisualBasic.FileIO;
using System.Linq;

namespace socconvertor.Services;

public class GoogleContactEntry
{
    public string DebtorCode { get; set; } = string.Empty; // from "First Name"
    public string CompanyName { get; set; } = string.Empty; // from "Organization Name"
    public string Notes { get; set; } = string.Empty;       // from "Notes"
    public List<string> To { get; set; } = new();           // labels: Work
    public List<string> Cc { get; set; } = new();           // labels: View
    public List<string> Bcc { get; set; } = new();          // labels: Private
}

public static class GoogleContactsCsvReader
{
    // Reads a Google Contacts CSV and returns entries keyed by Debtor Code (First Name)
    public static Dictionary<string, GoogleContactEntry> Parse(string csvPath)
    {
        var map = new Dictionary<string, GoogleContactEntry>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(csvPath) || !File.Exists(csvPath)) return map;

        using var parser = new TextFieldParser(csvPath)
        {
            TextFieldType = FieldType.Delimited,
            Delimiters = new[] { "," },
            HasFieldsEnclosedInQuotes = true
        };

        var headers = parser.ReadFields() ?? Array.Empty<string>();
        var headerIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headers.Length; i++)
        {
            headerIndex[(headers[i] ?? string.Empty).Trim()] = i;
        }

        int idxFirstName = GetIndex(headerIndex, "First Name");
        int idxOrgName   = GetIndex(headerIndex, "Organization Name");
        int idxNotes     = GetIndex(headerIndex, "Notes");

        // Email labels and values can be multiple: E-mail N - Label / E-mail N - Value
        var emailPairs = new List<(int labelIdx, int valueIdx)>();
        for (int n = 1; n <= 10; n++)
        {
            int li = GetIndex(headerIndex, $"E-mail {n} - Label");
            int vi = GetIndex(headerIndex, $"E-mail {n} - Value");
            if (li >= 0 && vi >= 0) emailPairs.Add((li, vi));
        }
        // Also handle possible variants "Email" without hyphen
        for (int n = 1; n <= 10; n++)
        {
            int li = GetIndex(headerIndex, $"Email {n} - Label");
            int vi = GetIndex(headerIndex, $"Email {n} - Value");
            if (li >= 0 && vi >= 0) emailPairs.Add((li, vi));
        }

        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields() ?? Array.Empty<string>();
            if (fields.Length == 0) continue;

            var debtor = SafeField(fields, idxFirstName);
            if (string.IsNullOrWhiteSpace(debtor)) continue;

            var entry = new GoogleContactEntry
            {
                DebtorCode = debtor.Trim(),
                CompanyName = SafeField(fields, idxOrgName),
                Notes = SafeField(fields, idxNotes)
            };

            foreach (var (labelIdx, valueIdx) in emailPairs)
            {
                var label = SafeField(fields, labelIdx);
                var email = SafeField(fields, valueIdx);
                if (string.IsNullOrWhiteSpace(email)) continue;
                switch ((label ?? string.Empty).Trim().ToLowerInvariant())
                {
                    case "work": entry.To.Add(email.Trim()); break;
                    case "view": entry.Cc.Add(email.Trim()); break;
                    case "private": entry.Bcc.Add(email.Trim()); break;
                    default:
                        // if no label or unknown, put into To by default
                        entry.To.Add(email.Trim());
                        break;
                }
            }

            var key = entry.DebtorCode.Trim();
            if (!map.TryGetValue(key, out var existing))
            {
                map[key] = entry;
            }
            else
            {
                // merge emails if duplicate debtor rows
                existing.CompanyName = string.IsNullOrWhiteSpace(existing.CompanyName) ? entry.CompanyName : existing.CompanyName;
                existing.Notes = string.IsNullOrWhiteSpace(existing.Notes) ? entry.Notes : existing.Notes;
                existing.To.AddRange(entry.To.Where(e => !existing.To.Contains(e, StringComparer.OrdinalIgnoreCase)));
                existing.Cc.AddRange(entry.Cc.Where(e => !existing.Cc.Contains(e, StringComparer.OrdinalIgnoreCase)));
                existing.Bcc.AddRange(entry.Bcc.Where(e => !existing.Bcc.Contains(e, StringComparer.OrdinalIgnoreCase)));
            }
        }

        return map;
    }

    private static int GetIndex(Dictionary<string,int> headerIndex, string name)
    {
        return headerIndex.TryGetValue(name, out var idx) ? idx : -1;
    }

    private static string SafeField(string[] fields, int idx)
    {
        return (idx >= 0 && idx < fields.Length) ? (fields[idx] ?? string.Empty).Trim() : string.Empty;
    }
}
