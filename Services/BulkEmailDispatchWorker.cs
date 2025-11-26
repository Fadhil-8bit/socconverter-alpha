using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using socconvertor.Models.Email;
using socconvertor.Models.BulkEmail;
using System.Threading.Channels;

namespace socconvertor.Services;

public class BulkEmailDispatchWorker : BackgroundService
{
    private readonly IBulkEmailDispatchQueue _queue;
    private readonly IBulkEmailService _bulkService;
    private readonly ILogger<BulkEmailDispatchWorker> _logger;
    private readonly IConfiguration _config;
    private readonly IEmailSender _emailSender;

    public BulkEmailDispatchWorker(IBulkEmailDispatchQueue queue, IBulkEmailService bulkService, ILogger<BulkEmailDispatchWorker> logger, IConfiguration config, IEmailSender emailSender)
    {
        _queue = queue;
        _bulkService = bulkService;
        _logger = logger;
        _config = config;
        _emailSender = emailSender;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BulkEmailDispatchWorker started");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessJobsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in dispatch loop");
            }
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }

    private async Task ProcessJobsAsync(CancellationToken ct)
    {
        var jobs = _queue.GetAllJobs().Where(j => j.Status == EmailDispatchJobStatus.Queued || j.Status == EmailDispatchJobStatus.Running || j.Status == EmailDispatchJobStatus.PartiallyDeferred).ToList();
        foreach (var job in jobs)
        {
            if (ct.IsCancellationRequested) return;
            if (job.NextResumeUtc.HasValue && job.NextResumeUtc.Value > DateTime.UtcNow)
                continue; // wait until resume time
            if (job.Status == EmailDispatchJobStatus.Cancelled) { continue; }

            job.Status = EmailDispatchJobStatus.Running;
            _queue.UpdateJob(job);

            int maxPerHour = int.Parse(_config["Email:RateLimit:MaxPerHour"] ?? "0");
            int delayMs = int.Parse(_config["Email:RateLimit:DelayMs"] ?? "0");
            int maxPerMinute = int.Parse(_config["Email:RateLimit:MaxPerMinute"] ?? "0");
            int maxPerDay = int.Parse(_config["Email:RateLimit:MaxPerDay"] ?? "0");
            int batchSize = int.Parse(_config["Email:RateLimit:BatchSize"] ?? "50");

            // Compute adaptive per-item delay from rate caps
            int calcDelayFromMinute = maxPerMinute > 0 ? (int)Math.Ceiling(60000.0 / maxPerMinute) : 0;
            int calcDelayFromHour = maxPerHour > 0 ? (int)Math.Ceiling(3600000.0 / maxPerHour) : 0;
            int perItemDelayMs = Math.Max(delayMs, Math.Max(calcDelayFromMinute, calcDelayFromHour));

            int sentThisMinute = 0; DateTime minuteWindow = DateTime.UtcNow;
            int sentThisHour = 0; DateTime hourWindow = DateTime.UtcNow;
            DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
            int sentToday = job.Items.Count(i => i.Status == EmailDispatchItemStatus.Sent && i.LastAttemptUtc.HasValue && DateOnly.FromDateTime(i.LastAttemptUtc.Value) == today);
            int processedInBatch = 0;

            foreach (var item in job.Items.Where(i => i.Status == EmailDispatchItemStatus.Pending || i.Status == EmailDispatchItemStatus.Deferred))
            {
                if (ct.IsCancellationRequested) return;

                var now = DateTime.UtcNow;
                if ((now - minuteWindow).TotalMinutes >= 1) { minuteWindow = now; sentThisMinute = 0; }
                if ((now - hourWindow).TotalHours >= 1) { hourWindow = now; sentThisHour = 0; }

                // Daily cap check
                if (maxPerDay > 0 && sentToday >= maxPerDay)
                {
                    job.Status = EmailDispatchJobStatus.PartiallyDeferred;
                    job.NextResumeUtc = DateTime.UtcNow.Date.AddDays(1).AddMinutes(5);
                    _queue.UpdateJob(job);
                    _logger.LogInformation("Job {JobId} hit daily cap ({MaxPerDay}). Resume at {NextResumeUtc}", job.JobId, maxPerDay, job.NextResumeUtc);
                    break;
                }

                if (maxPerHour > 0 && sentThisHour >= maxPerHour)
                {
                    job.Status = EmailDispatchJobStatus.PartiallyDeferred;
                    job.NextResumeUtc = hourWindow.AddHours(1);
                    _queue.UpdateJob(job);
                    _logger.LogInformation("Job {JobId} hit hourly cap ({MaxPerHour}). Resume at {NextResumeUtc}", job.JobId, maxPerHour, job.NextResumeUtc);
                    break;
                }
                if (maxPerMinute > 0 && sentThisMinute >= maxPerMinute)
                {
                    await Task.Delay(Math.Max(perItemDelayMs, 200), ct);
                    minuteWindow = DateTime.UtcNow; sentThisMinute = 0;
                }

                item.Status = EmailDispatchItemStatus.Sending;
                item.LastAttemptUtc = DateTime.UtcNow;
                _queue.UpdateJob(job);

                try
                {
                    // Assemble EmailOptions from config
                    var options = new EmailOptions
                    {
                        FromAddress = _config["Email:FromAddress"] ?? "noreply@example.com",
                        FromName = _config["Email:FromName"] ?? "PDF Reader Demo",
                        IsHtml = true,
                        MaxAttachmentSizeMB = int.Parse(_config["Email:MaxAttachmentSizeMB"] ?? "10"),
                        SmtpSettings = new SmtpSettings
                        {
                            Host = _config["Email:Smtp:Host"] ?? "localhost",
                            Port = int.Parse(_config["Email:Smtp:Port"] ?? "25"),
                            Username = _config["Email:Smtp:Username"] ?? "",
                            Password = _config["Email:Smtp:Password"] ?? "",
                            EnableSsl = bool.Parse(_config["Email:Smtp:EnableSsl"] ?? "false")
                        }
                    };

                    // Build attachments from job item paths
                    var attachments = item.AttachmentPaths.Select(p => EmailAttachment.FromFile(p)).ToList();
                    string subjectTemplate = job.SubjectTemplate;
                    string bodyTemplate = job.BodyTemplate;
                    string formattedSize = (item.TotalSizeBytes / (1024.0 * 1024.0)).ToString("F1") + " MB";
                    string subject = subjectTemplate
                        .Replace("{DebtorCode}", item.DebtorCode)
                        .Replace("{FileCount}", item.AttachmentCount.ToString())
                        .Replace("{TotalSize}", formattedSize);
                    string body = bodyTemplate
                        .Replace("{DebtorCode}", item.DebtorCode)
                        .Replace("{FileCount}", item.AttachmentCount.ToString())
                        .Replace("{TotalSize}", formattedSize);

                    // Send with built-in retry logic (EmailSenderService handles retries)
                    await _emailSender.SendEmailWithAttachmentsAsync(
                        item.EmailAddress,
                        subject,
                        body,
                        attachments,
                        options
                    );

                    item.Status = EmailDispatchItemStatus.Sent;
                    item.Error = null;
                }
                catch (Exception ex)
                {
                    item.Status = EmailDispatchItemStatus.Failed;
                    item.Error = ex.Message;
                    _logger.LogError(ex, "Failed to send email to {DebtorCode} ({Email})", item.DebtorCode, item.EmailAddress);
                }

                // Increment counters only for successful sends
                if (item.Status == EmailDispatchItemStatus.Sent)
                {
                    sentThisMinute++;
                    sentThisHour++;
                    sentToday++;
                    processedInBatch++;
                }

                // Batch checkpoint and delay
                if (batchSize > 0 && processedInBatch >= batchSize)
                {
                    _queue.UpdateJob(job);
                    processedInBatch = 0;
                    _logger.LogDebug("Job {JobId} batch checkpoint: {Sent} sent", job.JobId, job.SuccessCount);
                }

                // Apply adaptive per-item delay
                if (perItemDelayMs > 0)
                {
                    await Task.Delay(perItemDelayMs, ct);
                }

                _queue.UpdateJob(job);
            }

            if (job.PendingCount == 0 && job.DeferredCount == 0)
            {
                job.Status = EmailDispatchJobStatus.Completed;
                job.CompletedUtc = DateTime.UtcNow;
                _queue.UpdateJob(job);
                _logger.LogInformation("Job {JobId} completed. Sent={Sent}, Failed={Failed}", job.JobId, job.SuccessCount, job.FailedCount);
            }
        }
    }
}
