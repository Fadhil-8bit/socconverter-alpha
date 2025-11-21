using Microsoft.Extensions.Configuration;

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
}
