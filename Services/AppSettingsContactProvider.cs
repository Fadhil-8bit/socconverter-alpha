namespace socconvertor.Services;

public class AppSettingsContactProvider : IContactProvider
{
    private readonly IConfiguration _config;
    private readonly Dictionary<string, string> _cache;

    public AppSettingsContactProvider(IConfiguration config)
    {
        _config = config;
        _cache = new(StringComparer.OrdinalIgnoreCase);

        // Load once from configuration section: Contacts:Debtors:{Code} = email
        var section = _config.GetSection("Contacts:Debtors");
        foreach (var child in section.GetChildren())
        {
            var code = child.Key;
            var email = child.Value;
            if (!string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(email))
            {
                _cache[code] = email;
            }
        }
    }

    public string? GetEmailForDebtor(string debtorCode)
    {
        if (string.IsNullOrWhiteSpace(debtorCode)) return null;
        return _cache.TryGetValue(debtorCode, out var email) ? email : null;
    }

    public GoogleContactEntry? GetDetails(string debtorCode)
    {
        var email = GetEmailForDebtor(debtorCode);
        if (string.IsNullOrWhiteSpace(email)) return null;
        return new GoogleContactEntry
        {
            DebtorCode = debtorCode,
            CompanyName = string.Empty,
            Notes = string.Empty,
            To = new List<string> { email }
        };
    }
}
