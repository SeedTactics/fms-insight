using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;

namespace BlackMaple.FMSInsight.Client;

/// <summary>JSON options for the Insight HTTP wire format, independent of the server assembly.</summary>
public static class InsightJson
{
  public static JsonSerializerOptions CreateOptions()
  {
    var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
      PropertyNamingPolicy = null,
      DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
      RespectNullableAnnotations = true,
      AllowTrailingCommas = true,
    };
    options.Converters.Add(new JsonStringEnumConverter());
    options.Converters.Add(new DurationConverter());
    return options;
  }

  private sealed class DurationConverter : JsonConverter<TimeSpan>
  {
    public override TimeSpan Read(
      ref Utf8JsonReader reader,
      Type typeToConvert,
      JsonSerializerOptions options
    )
    {
      if (reader.TokenType != JsonTokenType.String)
        throw new JsonException("Expected an Insight duration string.");
      var value = reader.GetString()!;
      if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var duration))
        return duration;
      try
      {
        return XmlConvert.ToTimeSpan(value);
      }
      catch (Exception e) when (e is FormatException or OverflowException)
      {
        throw new JsonException("Invalid Insight duration.", e);
      }
    }

    public override void Write(
      Utf8JsonWriter writer,
      TimeSpan value,
      JsonSerializerOptions options
    ) => writer.WriteStringValue(XmlConvert.ToString(value));
  }
}
