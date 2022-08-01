using Azure.Core;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using Azure.Storage.Queues;
using AzureBot.Bot.Configuration;
using AzureBot.Bot.Telemetry;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenTelemetry.Trace;
using System;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var services = builder.Services;
services.AddControllers();
services.AddOpenTelemetryTracing((tracing) =>
{
    tracing.AddAspNetCoreInstrumentation();
    tracing.AddHttpClientInstrumentation();
    tracing.AddAzureMonitorTraceExporter((exporter) =>
    {
        exporter.ConnectionString = builder.Configuration["AzureMonitor:ConnectionString"];
    });
});
services.Configure<AzureBotOptions>(builder.Configuration.GetSection("AzureBot"));
services.AddSingleton<TokenCredential>((sp) =>
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
services.AddSingleton((sp) =>
{
    var appOptions = sp.GetRequiredService<IOptionsMonitor<AzureBotOptions>>().CurrentValue;
    var credentials = sp.GetRequiredService<TokenCredential>();
    return new QueueServiceClient(new Uri(appOptions.QueueUrl), credentials);
});
services.AddSingleton((sp) =>
{
    var appOptions = sp.GetRequiredService<IOptionsMonitor<AzureBotOptions>>().CurrentValue;
    var credentials = sp.GetRequiredService<TokenCredential>();
    return new CosmosClient(appOptions.CosmosUrl, credentials);
});
services.AddSingleton<ActivityManager>();

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