using AzureBot.Deploy.Configuration;
using AzureBot.Deploy.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace AzureBot.Deploy.Commands.Discord;

internal class UpdateCommand : ICommandHandler
{
    private static readonly Option<InstanceConfig> _instanceOption = new(new[] { "--instance", "-i" }, InstanceConfig.FromArgument, false, "The configuration file for the instance you are deploying") { IsRequired = true };
    private static readonly Option<string> _commandNameOption = new(new[] { "--name", "-n" }, "The command to update") { IsRequired = true };
    private static readonly Option<string> _guildNameOption = new(
        new[] { "--guild-name", "-g" },
        "The name of the guild to register to. If omitted, the command will be registered globally. Must be one of the WellKnownGuilds in the instance configuration.");

    public static Command GetCommand(IServiceProvider serviceProvider)
    {
        var command = new Command("update", "Creates or updates a discord bot command")
        {
            _instanceOption,
            _commandNameOption,
            _guildNameOption,
        };
        command.Handler = ActivatorUtilities.CreateInstance<UpdateCommand>(serviceProvider);
        return command;
    }

    private readonly JsonObject _commands = new()
    {
        ["hello-world"] = new JsonObject
        {
            ["name"] = "hello-world",
            ["description"] = "A basic command",
            ["type"] = 1, // CHAT_INPUT
        },
        ["azurebot"] = new JsonObject
        {
            ["name"] = "azurebot",
            ["description"] = "Commands for working with AzureBot",
            ["type"] = 1, // CHAT_INPUT
            ["options"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "server",
                    ["description"] = "Commands for working with AzureBot servers",
                    ["type"] = 2, // SUB_COMMAND_GROUP
                    ["options"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["name"] = "start",
                            ["description"] = "Starts a server",
                            ["type"] = 1, // SUB_COMMAND
                            ["options"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["name"] = "name",
                                    ["description"] = "The name of the server",
                                    ["type"] = 3, // STRING
                                    ["required"] = true,
                                },
                            },
                        },
                        new JsonObject
                        {
                            ["name"] = "stop",
                            ["description"] = "Stops a server",
                            ["type"] = 1, // SUB_COMMAND
                            ["options"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["name"] = "name",
                                    ["description"] = "The name of the server",
                                    ["type"] = 3, // STRING
                                    ["required"] = true,
                                },
                            },
                        },
                    },
                },
            },
        },
    };
    private readonly ILogger<UpdateCommand> _logger;
    private readonly DiscordClient _discord;

    public UpdateCommand(ILogger<UpdateCommand> logger, DiscordClient discord)
    {
        _logger = logger;
        _discord = discord;
    }

    public async Task<int> InvokeAsync(InvocationContext context)
    {
        var cancellationToken = context.GetCancellationToken();
        var instance = context.ParseResult.GetValueForOption(_instanceOption)!;
        var name = context.ParseResult.GetValueForOption(_commandNameOption)!;
        var guildName = context.ParseResult.GetValueForOption(_guildNameOption);

        if (context.ParseResult.Errors.Any())
        {
            foreach (var error in context.ParseResult.Errors)
            {
                _logger.LogError("{}", error.Message);
            }

            return 1;
        }

        await _discord.NewCommandAsync(instance.Discord, guildName, _commands[name].Deserialize<JsonElement>(), cancellationToken);

        return 0;
    }
}
