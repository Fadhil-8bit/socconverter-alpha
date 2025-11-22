namespace socconvertor.Helpers;

/// <summary>
/// Helper methods for consistent date parsing and normalization used across services.
/// </summary>
public static class DateHelpers
{
    /// <summary>
    /// Try to parse a date-like string into normalized yyyy-MM-dd format.
    /// Returns null if parsing fails.
    /// This method is conservative and uses invariant culture.
    /// </summary>
    public static string? NormalizeToIsoDate(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        input = input.Trim();

        // Try common parse with InvariantCulture
        if (DateTime.TryParse(input, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeLocal, out var dt))
            return dt.ToString("yyyy-MM-dd");

        // Try a few common explicit formats (day/month/year, year-month-day)
        var formats = new[] { "d/M/yyyy", "dd/MM/yyyy", "d-M-yyyy", "dd-MM-yyyy", "yyyy-MM-dd", "yyyyMMdd", "d MMM yyyy", "dd MMM yyyy" };
        if (DateTime.TryParseExact(input, formats, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out dt))
            return dt.ToString("yyyy-MM-dd");

        // Last-resort: try to extract a year-month group like YYYYMM
        var digits = System.Text.RegularExpressions.Regex.Match(input, @"(?<y>\d{4})(?<m>\d{2})");
        if (digits.Success)
        {
            var y = digits.Groups["y"].Value;
            var m = digits.Groups["m"].Value;
            if (int.TryParse(m, out var mi) && mi >= 1 && mi <= 12)
            {
                return y + "-" + m + "-01";
            }
        }

        return null;
    }
}
