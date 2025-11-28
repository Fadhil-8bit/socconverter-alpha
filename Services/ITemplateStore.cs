using socconvertor.Models.BulkEmail;

namespace socconvertor.Services;

public interface ITemplateStore
{
    Task<List<EmailTemplate>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<EmailTemplate> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<EmailTemplate> SaveAsync(EmailTemplate template, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
    Task<bool> ClearDefaultAsync(bool overdueCategory, CancellationToken cancellationToken = default);
}
