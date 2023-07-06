using Azure.Core;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Azure.Storage.Queues;
using AzureBot.Bot.Configuration;
using AzureBot.Bot.Telemetry;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System;

var builder = WebApplication.CreateBuilder(args);

TokenCredential credential;
if (builder.Environment.IsProduction())
{
    credential = new ManagedIdentityCredential();
}
else
{
    credential = new DefaultAzureCredential();
}

// Add services to the container.
var services = builder.Services;

services.Configure<AzureBotOptions>(builder.Configuration.GetSection("AzureBot"));
services.AddControllers();
services
    .AddOpenTelemetry()
    .UseAzureMonitor((options) =>
    {
        options.Credential = credential;
    });
services.AddSingleton(credential);
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