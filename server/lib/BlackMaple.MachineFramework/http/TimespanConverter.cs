using System;
using Newtonsoft.Json;

namespace BlackMaple.MachineFramework
{
  public class TimespanConverter : JsonConverter
  {
    public override bool CanConvert(Type objectType)
    {
      return objectType == typeof(TimeSpan);
    }

    public override bool CanRead => true;
    public override bool CanWrite => true;

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
      if (objectType != typeof(TimeSpan))
        throw new ArgumentException();

      var spanString = reader.Value as string;
      if (spanString == null)
        return null;
      if (TimeSpan.TryParse(spanString, out TimeSpan result))
        return result;
      return System.Xml.XmlConvert.ToTimeSpan(spanString);
    }

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
      var duration = (TimeSpan)value;
      writer.WriteValue(System.Xml.XmlConvert.ToString(duration));
    }
  }
}