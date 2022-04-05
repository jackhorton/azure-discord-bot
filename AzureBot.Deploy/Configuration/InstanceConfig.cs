using Azure.Core;
using System;
using System.CommandLine.Parsing;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AzureBot.Deploy.Configuration;

public record InstanceConfig
{
    public Guid SubscriptionId { get; init; }
    [Required]
    public string ResourceGroupName { get; init; } = default!;
    [JsonConverter(typeof(StringWrapperJsonConverter<AzureLocation>))]
    public AzureLocation Location { get; init; } = default!;
    public Guid AdminObjectId { get; init; }
    [Required]
    public string Domain { get; init; } = default!;
    public string ControllerName { get; init; } = default!;
    [Required]
    public DiscordAppConfig Discord { get; init; } = default!;
    [Required]
    public HttpsConfig Https { get; init; } = default!;

    public static InstanceConfig FromArgument(ArgumentResult result)
    {
        var path = result.Tokens.SingleOrDefault().Value;
        if (path is not { Length: > 0 })
        {
            result.ErrorMessage = "Non-empty path must be given";
            return default!;
        }
        using var file = File.OpenRead(path);
        var instance = JsonSerializer.Deserialize<InstanceConfig>(file) ?? throw new ArgumentException("Invalid instance configuration", nameof(result));

        var context = new ValidationContext(instance);
        Validator.ValidateObject(instance, context, validateAllProperties: true);

        return instance;
    }
}