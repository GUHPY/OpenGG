using System.Text.Json;
using System.Text.Json.Serialization;

namespace OneRGB.Application.Control;

/// <summary>Grava <see cref="ResourceId"/> como texto simples (<c>"gpu-msi-rtx5070-gaming-trio/rgb"</c>).</summary>
public sealed class ResourceIdJsonConverter : JsonConverter<ResourceId>
{
    public override ResourceId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetString() is { Length: > 0 } value ? value : throw new JsonException("Recurso vazio."));

    public override void Write(Utf8JsonWriter writer, ResourceId value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.Value);
    }

    public override ResourceId ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Read(ref reader, typeToConvert, options);

    public override void WriteAsPropertyName(Utf8JsonWriter writer, ResourceId value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WritePropertyName(value.Value);
    }
}

/// <summary>Grava <see cref="ControlKey"/> como <c>"recurso#ajuste"</c>, também como chave de dicionário (perfis).</summary>
public sealed class ControlKeyJsonConverter : JsonConverter<ControlKey>
{
    public override ControlKey Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        ControlKey.TryParse(reader.GetString(), out var key) ? key : throw new JsonException("Invalid control key (expected \"resource#setting\").");

    public override void Write(Utf8JsonWriter writer, ControlKey value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.ToString());
    }

    public override ControlKey ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Read(ref reader, typeToConvert, options);

    public override void WriteAsPropertyName(Utf8JsonWriter writer, ControlKey value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WritePropertyName(value.ToString());
    }
}
