using System.Text.Json;
using socconvertor.Models.BulkEmail;

namespace socconvertor.Services;

public interface IEmailDraftStore
{
    Task<EmailDrafts> GetAsync(CancellationToken ct = default);
    Task SaveAsync(EmailDrafts drafts, CancellationToken ct = default);
}

public class FileEmailDraftStore : IEmailDraftStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1,1);

    public FileEmailDraftStore(IConfiguration config)
    {
        var configured = config["EmailDrafts:FilePath"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            _path = Path.GetFullPath(configured);
        }
        else
        {
            var dataDir = Path.Combine(Directory.GetCurrentDirectory(), "App_Data");
            Directory.CreateDirectory(dataDir);
            _path = Path.Combine(dataDir, "emailDrafts.json");
        }
    }

    public async Task<EmailDrafts> GetAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (!File.Exists(_path)) return new EmailDrafts();
            using var s = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<EmailDrafts>(s, cancellationToken: ct) ?? new EmailDrafts();
        }
        finally { _lock.Release(); }
    }

    public async Task SaveAsync(EmailDrafts drafts, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            using var s = File.Create(_path);
            await JsonSerializer.SerializeAsync(s, drafts, new JsonSerializerOptions{ WriteIndented = true }, ct);
            await s.FlushAsync(ct);
        }
        finally { _lock.Release(); }
    }
}
