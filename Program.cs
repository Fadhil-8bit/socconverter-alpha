using socconvertor.Services;


var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Register existing services
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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
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
