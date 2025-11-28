using System.Text.Json;
using socconvertor.Models.BulkEmail;

namespace socconvertor.Services;

public class FileTemplateStore : ITemplateStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1,1);

    public FileTemplateStore(IConfiguration config)
    {
        var configured = config["EmailTemplates:FilePath"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            _path = Path.GetFullPath(configured);
        }
        else
        {
            var dataDir = Path.Combine(Directory.GetCurrentDirectory(), "App_Data");
            Directory.CreateDirectory(dataDir);
            _path = Path.Combine(dataDir, "emailTemplates.json");
        }
    }

    public async Task<List<EmailTemplate>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_path)) return new List<EmailTemplate>();
            using var stream = File.OpenRead(_path);
            var list = await JsonSerializer.DeserializeAsync<List<EmailTemplate>>(stream, cancellationToken: cancellationToken)
                      ?? new List<EmailTemplate>();
            return list.OrderBy(t => t.Name).ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<EmailTemplate> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var all = await GetAllAsync(cancellationToken);
        return all.FirstOrDefault(t => t.Id == id) ?? new EmailTemplate();
    }

    public async Task<EmailTemplate> SaveAsync(EmailTemplate template, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var list = new List<EmailTemplate>();
            if (File.Exists(_path))
            {
                using var r = File.OpenRead(_path);
                list = await JsonSerializer.DeserializeAsync<List<EmailTemplate>>(r, cancellationToken: cancellationToken)
                       ?? new List<EmailTemplate>();
            }

            // enforce contextual defaults: only one per category
            if (template.DefaultForSoaInvoice)
                list.ForEach(t => t.DefaultForSoaInvoice = false);
            if (template.DefaultForOverdue)
                list.ForEach(t => t.DefaultForOverdue = false);

            var existing = list.FirstOrDefault(t => t.Id == template.Id);
            if (existing == null)
            {
                template.Id = string.IsNullOrWhiteSpace(template.Id) ? Guid.NewGuid().ToString("N") : template.Id;
                template.CreatedUtc = DateTime.UtcNow;
                template.ModifiedUtc = DateTime.UtcNow;
                list.Add(template);
            }
            else
            {
                existing.Name = template.Name;
                existing.Subject = template.Subject;
                existing.Body = template.Body;
                existing.DefaultForSoaInvoice = template.DefaultForSoaInvoice;
                existing.DefaultForOverdue = template.DefaultForOverdue;
                existing.ModifiedUtc = DateTime.UtcNow;
            }

            using var w = File.Create(_path);
            await JsonSerializer.SerializeAsync(w, list, new JsonSerializerOptions { WriteIndented = true }, cancellationToken);
            await w.FlushAsync(cancellationToken);
            return template;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_path)) return false;
            using var r = File.OpenRead(_path);
            var list = await JsonSerializer.DeserializeAsync<List<EmailTemplate>>(r, cancellationToken: cancellationToken)
                       ?? new List<EmailTemplate>();
            var removed = list.RemoveAll(t => t.Id == id) > 0;
            using var w = File.Create(_path);
            await JsonSerializer.SerializeAsync(w, list, new JsonSerializerOptions { WriteIndented = true }, cancellationToken);
            await w.FlushAsync(cancellationToken);
            return removed;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> ClearDefaultAsync(bool overdueCategory, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_path)) return false;
            using var r = File.OpenRead(_path);
            var list = await JsonSerializer.DeserializeAsync<List<EmailTemplate>>(r, cancellationToken: cancellationToken)
                       ?? new List<EmailTemplate>();
            if (overdueCategory)
                list.ForEach(t => t.DefaultForOverdue = false);
            else
                list.ForEach(t => t.DefaultForSoaInvoice = false);
            using var w = File.Create(_path);
            await JsonSerializer.SerializeAsync(w, list, new JsonSerializerOptions { WriteIndented = true }, cancellationToken);
            await w.FlushAsync(cancellationToken);
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }
}
