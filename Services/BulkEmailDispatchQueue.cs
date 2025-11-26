using socconvertor.Models.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using socconvertor.Models.BulkEmail;

namespace socconvertor.Services;

public interface IBulkEmailDispatchQueue
{
    EmailDispatchJob EnqueueFromSession(BulkEmailSession session);
    EmailDispatchJob? GetJob(string jobId);
    IEnumerable<EmailDispatchJob> GetAllJobs();
    void UpdateJob(EmailDispatchJob job);
    void CancelJob(string jobId, string? reason = null);
    int ClearJobs(bool onlyCompletedOrCancelled);
}

public class BulkEmailDispatchQueue : IBulkEmailDispatchQueue
{
    private readonly List<EmailDispatchJob> _jobs = new();
    private readonly object _lock = new();
    private readonly string _jobsPath;
    private readonly ILogger<BulkEmailDispatchQueue> _logger;

    public BulkEmailDispatchQueue(IHostEnvironment env, ILogger<BulkEmailDispatchQueue> logger)
    {
        _logger = logger;
        _jobsPath = Path.Combine(env.ContentRootPath, "Data", "Jobs");
        Directory.CreateDirectory(_jobsPath);
        LoadExisting();
    }

    private void LoadExisting()
    {
        try
        {
            foreach (var file in Directory.GetFiles(_jobsPath, "*.json"))
            {
                var json = File.ReadAllText(file);
                var job = EmailDispatchJob.FromJson(json);
                if (job != null)
                    _jobs.Add(job);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load existing jobs");
        }
    }

    private void Persist(EmailDispatchJob job)
    {
        try
        {
            File.WriteAllText(Path.Combine(_jobsPath, job.JobId + ".json"), job.ToJson());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist job {JobId}", job.JobId);
        }
    }

    public EmailDispatchJob EnqueueFromSession(BulkEmailSession session)
    {
        var job = new EmailDispatchJob();
        foreach (var g in session.DebtorGroups)
        {
            job.Items.Add(new EmailDispatchItem
            {
                DebtorCode = g.DebtorCode,
                EmailAddress = g.EmailAddress,
                TotalSizeBytes = g.TotalSizeBytes,
                AttachmentCount = g.Attachments.Count,
                AttachmentPaths = g.Attachments.Select(a => a.FilePath).ToList()
            });
        }
        lock (_lock)
        {
            _jobs.Add(job);
            Persist(job);
        }
        return job;
    }

    public EmailDispatchJob? GetJob(string jobId)
    {
        lock (_lock) return _jobs.FirstOrDefault(j => j.JobId == jobId);
    }

    public IEnumerable<EmailDispatchJob> GetAllJobs()
    {
        lock (_lock) return _jobs.ToList();
    }

    public void UpdateJob(EmailDispatchJob job)
    {
        lock (_lock) Persist(job);
    }

    public void CancelJob(string jobId, string? reason = null)
    {
        lock (_lock)
        {
            var job = _jobs.FirstOrDefault(j => j.JobId == jobId);
            if (job == null) return;
            job.Cancel(reason ?? "Cancelled by user");
            Persist(job);
        }
    }

    public int ClearJobs(bool onlyCompletedOrCancelled)
    {
        lock (_lock)
        {
            var removable = _jobs.Where(j => !onlyCompletedOrCancelled || j.Status == EmailDispatchJobStatus.Completed || j.Status == EmailDispatchJobStatus.Cancelled || j.Status == EmailDispatchJobStatus.Failed).ToList();
            foreach (var job in removable)
            {
                var path = Path.Combine(_jobsPath, job.JobId + ".json");
                try { if (File.Exists(path)) File.Delete(path); } catch { }
                _jobs.Remove(job);
            }
            return removable.Count;
        }
    }
}
