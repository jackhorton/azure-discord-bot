using Azure.Core;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using Azure.Storage.Queues;
using AzureBot.Bot.Configuration;
using AzureBot.Bot.Telemetry;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenTelemetry.Trace;
using System;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddOpenTelemetryTracing((tracing) =>
{
    tracing.AddAspNetCoreInstrumentation();
    tracing.AddHttpClientInstrumentation();
    tracing.AddAzureMonitorTraceExporter((exporter) =>
    {
        exporter.ConnectionString = builder.Configuration["AzureMonitor:ConnectionString"];
    });
});
builder.Services.Configure<AzureBotOptions>(builder.Configuration.GetSection("AzureBot"));
builder.Services.AddSingleton<TokenCredential>((sp) =>
{
    var appOptions = sp.GetRequiredService<IOptionsMonitor<AzureBotOptions>>().CurrentValue;
    var credentialOptions = new DefaultAzureCredentialOptions
    {
        ManagedIdentityClientId = appOptions.ClientId,
        ExcludeInteractiveBrowserCredential = true,
        ExcludeSharedTokenCacheCredential = true,
        VisualStudioCodeTenantId = appOptions.TenantId,
        VisualStudioTenantId = appOptions.TenantId,
    };
    return new DefaultAzureCredential(credentialOptions);
});
builder.Services.AddSingleton<QueueServiceClient>((sp) =>
{
    var appOptions = sp.GetRequiredService<IOptionsMonitor<AzureBotOptions>>().CurrentValue;
    var credentials = sp.GetRequiredService<TokenCredential>();
    return new QueueServiceClient(new Uri(appOptions.QueueUrl), credentials);
});
builder.Services.AddSingleton<ActivityManager>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseAuthorization();

app.MapControllers();

app.Run();