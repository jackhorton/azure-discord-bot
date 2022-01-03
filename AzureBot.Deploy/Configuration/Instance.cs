using Azure.ResourceManager.Resources.Models;
using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace AzureBot.Deploy.Configuration;

public record Instance
{
    public Guid SubscriptionId { get; init; }
    [Required]
    public string ResourceGroupName { get; init; } = default!;
    [JsonConverter(typeof(StringWrapperJsonConverter<Location>))]
    public Location Location { get; init; }
    public Guid AdminObjectId { get; init; }
    [Required]
    public string Domain { get; init; } = default!;
    public string ControllerName { get; init; } = default!;
    public DiscordApp Discord { get; init; } = default!;
}