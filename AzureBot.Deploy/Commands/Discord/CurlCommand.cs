using AzureBot.Deploy.Configuration;
using AzureBot.Deploy.Services;
using AzureBot.Discord;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace AzureBot.Deploy.Commands.Discord;

internal class CurlCommand : ICommandHandler
{
    private static readonly Option<InstanceConfig> _instanceOption = new(new[] { "--instance", "-i" }, InstanceConfig.FromArgument, false, "The configuration file for the instance for this discord app") { IsRequired = true };
    private static readonly Option<string> _methodOption = new(new[] { "--method", "-X" }, () => "GET", "The HTTP method to use");
    private static readonly Argument<string> _urlArgument = new("url", "The API URL to make the request to");

    public static Command GetCommand(IServiceProvider serviceProvider)
    {
        var command = new Command("curl", "Make an arbitrary request to discord's API as a bot")
        {
            _urlArgument,
            _instanceOption,
            _methodOption,
        };
        command.Handler = ActivatorUtilities.CreateInstance<CurlCommand>(serviceProvider);
        return command;
    }

    private readonly ILogger<CurlCommand> _logger;
    private readonly IServiceProvider _serviceProvider;

    public CurlCommand(ILogger<CurlCommand> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public async Task<int> InvokeAsync(InvocationContext context)
    {
        var cancellationToken = context.GetCancellationToken();
        var instance = context.ParseResult.GetValueForOption(_instanceOption)!;
        var url = context.ParseResult.GetValueForArgument(_urlArgument)!;
        var method = context.ParseResult.GetValueForOption(_methodOption)!;

        var auth = ActivatorUtilities.CreateInstance<DiscordAuthentication>(_serviceProvider, instance);
        var discord = ActivatorUtilities.CreateInstance<DiscordClient>(_serviceProvider, auth);

        using var req = new HttpRequestMessage(new HttpMethod(method), url);
        if (method.Equals("GET", StringComparison.InvariantCultureIgnoreCase) && Console.IsInputRedirected)
        {
            throw new Exception();
        }
        else if (!method.Equals("GET", StringComparison.InvariantCultureIgnoreCase) && !Console.IsInputRedirected)
        {
            throw new Exception();
        }
        
        if (Console.IsInputRedirected)
        {
            req.Content = new StreamContent(Console.OpenStandardInput());
            req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        using var res = await discord.SendAsync(req, cancellationToken);

        var fullResponse = new StringBuilder($"{(int)res.StatusCode} {res.ReasonPhrase}");
        fullResponse.AppendLine();
        foreach (var header in res.Headers)
        {
            fullResponse.AppendLine($"{header.Key}: {string.Join(' ', header.Value)}");
        }
        fullResponse.AppendLine();
        fullResponse.AppendLine(await res.Content.ReadAsStringAsync(cancellationToken));

        Console.Error.WriteLine("{0}", fullResponse);

        return 0;
    }
}
