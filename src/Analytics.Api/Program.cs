using Analytics.Application.UseCases.ExecuteAnalyticsQuery;
using Analytics.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Add Razor Pages
builder.Services.AddRazorPages();

// Register application use-cases
builder.Services.AddScoped<ExecuteAnalyticsQueryUseCase>();

// Register infrastructure services
builder.Services.AddAiServiceClient(builder.Configuration);
builder.Services.AddSqlValidation(builder.Configuration);
builder.Services.AddDatabaseProfileResolution(builder.Configuration);
builder.Services.AddReadOnlyQueryExecution();
builder.Services.AddSchemaDiscovery();
builder.Services.AddFallbackHandler();
builder.Services.AddAnalyticsAuditLogging();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();

app.Run();
