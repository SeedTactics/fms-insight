using System;
using System.Linq;
using System.Collections.Generic;
using Xunit;
using FluentAssertions;
using BlackMaple.MachineFramework;
using BlackMaple.MachineWatchInterface;
using NSubstitute;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BlackMaple.FMSInsight.Niigata.Tests
{
  public class ConvertDebug
  {

    public class PalletLocConverter : JsonConverter<PalletLocation>
    {
      public override PalletLocation ReadJson(JsonReader reader, Type objectType, PalletLocation existingValue, bool hasExistingValue, JsonSerializer serializer)
      {
        var j = JObject.Load(reader);
        var loc = j.Value<string>("Location");
        var statGroup = j.Value<string>("StationGroup");
        var num = j.Value<int>("Num");
        return new PalletLocation((PalletLocationEnum)Enum.Parse(typeof(PalletLocationEnum), loc), statGroup, num);
      }

      public override void WriteJson(JsonWriter writer, PalletLocation value, JsonSerializer serializer)
      {
        throw new NotImplementedException();
      }
    }

    public class NiigataNameConverter : Newtonsoft.Json.Serialization.ISerializationBinder
    {
      public void BindToName(Type serializedType, out string assemblyName, out string typeName)
      {
        throw new NotImplementedException();
      }

      public Type BindToType(string assemblyName, string typeName)
      {
        if (assemblyName == null)
        {
          var t = Type.GetType("BlackMaple.FMSInsight.Niigata." + typeName + ", BlackMaple.FMSInsight.Niigata");
          if (t != null) return t;
          t = Type.GetType("BlackMaple.MachineWatchInterface." + typeName + ", BlackMaple.MachineFramework");
          return t;
        }
        else
        {
          return Type.GetType(typeName + ", " + assemblyName);
        }
      }

    }

    /*
        [Fact]
        public void Convert()
        {
          var st = new FMSSettings();

          var jdbCfg = JobDB.Config.InitializeJobDatabase("../../../niigata/data/niigatajobs.db");
          var logCfg = EventLogDB.Config.InitializeEventDatabase(st, "../../../niigata/data/niigatalog.db");

          var iccStatus = JsonConvert.DeserializeObject<NiigataStatus>(
            System.IO.File.ReadAllText("../../../niigata/data/status.json"),
            new JsonSerializerSettings()
            {
              TypeNameHandling = TypeNameHandling.All,
              MetadataPropertyHandling = MetadataPropertyHandling.ReadAhead,
              SerializationBinder = new NiigataNameConverter(),
              Converters = new[] { new PalletLocConverter() }
            }
          );

          var convert = new CreateCellState(st, names, null);

          using (var jdb = jdbCfg.OpenConnection())
          using (var ldb = logCfg.OpenConnection())
          {
            var jobs = jdb.LoadUnarchivedJobs();
            var cellSt = convert.BuildCellState(jdb, ldb, iccStatus, jobs);
          }
        }
        */
  }
}