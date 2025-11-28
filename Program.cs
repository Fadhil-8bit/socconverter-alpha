using socconvertor.Services;
using Microsoft.AspNetCore.Http.Features;


var builder = WebApplication.CreateBuilder(args);

// Increase form and multipart limits early to allow large debtor postings
builder.Services.Configure<FormOptions>(o =>
{
    o.ValueCountLimit = 20000;               // default 1024; raise for large forms
    o.ValueLengthLimit = int.MaxValue;       // max length per value
    o.BufferBody = false;                    // avoid buffering entire request body for large uploads
    o.MultipartBodyLengthLimit = 500 * 1024 * 1024; // 500 MB
    o.MultipartHeadersLengthLimit = 128 * 1024;
});

// Also ensure Kestrel doesn't enforce a smaller limit at the server layer
builder.WebHost.UseKestrel(o =>
{
    // Allow unlimited request body size at Kestrel level for large uploads (Nullable<long>)
    o.Limits.MaxRequestBodySize = null;
});

// Add services to the container.
builder.Services.AddControllersWithViews();

// Register existing services
builder.Services.AddSingleton<IStoragePaths, StoragePaths>();
builder.Services.AddSingleton<PdfService>();
builder.Services.AddSingleton<UploadFolderService>();

// Register contact provider: CSV if configured, else appsettings
if (!string.IsNullOrWhiteSpace(builder.Configuration["Contacts:Csv:Path"]))
{
    builder.Services.AddSingleton<IContactProvider, CsvContactProvider>();
}
else
{
    builder.Services.AddSingleton<IContactProvider, AppSettingsContactProvider>();
}

// Register bulk email services
builder.Services.AddSingleton<IBulkEmailService, BulkEmailService>();
builder.Services.AddSingleton<IEmailSender, EmailSenderService>();

// Queue and workers
builder.Services.AddSingleton<IBulkEmailDispatchQueue, BulkEmailDispatchQueue>();
builder.Services.AddHostedService<BulkEmailDispatchWorker>();

// Add hosted services
builder.Services.AddHostedService<BulkEmailRetentionService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
