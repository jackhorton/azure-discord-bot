using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Text.Json;

namespace AzureBot.Deploy.Configuration;

internal class InstanceParameter
{
    public InstanceConfig Instance { get; }
    public InstanceParameter(string path)
    {
        using var file = File.OpenRead(path);
        Instance = JsonSerializer.Deserialize<InstanceConfig>(file) ?? throw new ArgumentException("Invalid instance configuration", nameof(path));

        var context = new ValidationContext(Instance);
        Validator.ValidateObject(Instance, context, validateAllProperties: true);
    }
}
