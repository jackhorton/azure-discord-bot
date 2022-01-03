using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AzureBot.Deploy.Configuration;

internal class StringWrapperJsonConverter<TWrapping> : JsonConverter<TWrapping>
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert == typeof(TWrapping);
    }

    public override bool HandleNull => true;

    public override TWrapping? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {

        var ctor = typeof(TWrapping).GetConstructor(new[] { typeof(string) });
        if (ctor is null)
        {
            throw new Exception();
        }

        return (TWrapping)ctor.Invoke(new[] { reader.GetString() });
    }

    public override void Write(Utf8JsonWriter writer, TWrapping value, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }
}
