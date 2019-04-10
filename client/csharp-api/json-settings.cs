using System;
using Newtonsoft.Json;

namespace BlackMaple.FMSInsight.API
{
  public class TimespanConverter : JsonConverter
  {
    public override bool CanConvert(Type objectType)
    {
      return objectType == typeof(TimeSpan) || objectType == typeof(Nullable<TimeSpan>);
    }

    public override bool CanRead => true;
    public override bool CanWrite => true;

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
      if (!CanConvert(objectType)) throw new ArgumentException();

      if (objectType == typeof(Nullable<TimeSpan>) && reader.Value == null)
        return (Nullable<TimeSpan>)null;
      else if (reader.Value == null)
        throw new Exception("Invalid null value for TimeSpan");

      var spanString = reader.Value as string;
      if (TimeSpan.TryParse(spanString, out TimeSpan result)) return result;
      return System.Xml.XmlConvert.ToTimeSpan(spanString);
    }

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
      if (value == null) {
        writer.WriteNull();
      } else {
        var duration = (TimeSpan)value;
        writer.WriteValue(System.Xml.XmlConvert.ToString(duration));
      }
    }
  }

  public partial class JobsClient
  {
    partial void UpdateJsonSerializerSettings(Newtonsoft.Json.JsonSerializerSettings settings)
    {
      settings.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
      settings.Converters.Add(new TimespanConverter());
    }
  }

  public partial class LogClient
  {
    partial void UpdateJsonSerializerSettings(Newtonsoft.Json.JsonSerializerSettings settings)
    {
      settings.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
      settings.Converters.Add(new TimespanConverter());
    }
  }

  public partial class FmsClient
  {
    partial void UpdateJsonSerializerSettings(Newtonsoft.Json.JsonSerializerSettings settings)
    {
      settings.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
      settings.Converters.Add(new TimespanConverter());
    }
  }
}
