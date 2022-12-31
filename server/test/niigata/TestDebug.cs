using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Xunit;
using FluentAssertions;
using BlackMaple.MachineFramework;
using NSubstitute;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

#nullable enable

namespace BlackMaple.FMSInsight.Niigata.Tests
{
  public class TestDebug
  {
    public class PalletLocConverter : JsonConverter<PalletLocation>
    {
      public override PalletLocation ReadJson(
        JsonReader reader,
        Type objectType,
        PalletLocation? existingValue,
        bool hasExistingValue,
        JsonSerializer serializer
      )
      {
        var j = JObject.Load(reader);
        var loc = j.Value<string>("Location")!;
        var statGroup = j.Value<string>("StationGroup")!;
        var num = j.Value<int>("Num");
        return new PalletLocation(
          (PalletLocationEnum)Enum.Parse(typeof(PalletLocationEnum), loc),
          statGroup,
          num
        );
      }

      public override void WriteJson(JsonWriter writer, PalletLocation? value, JsonSerializer serializer)
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

      public Type BindToType(string? assemblyName, string typeName)
      {
        if (assemblyName == null)
        {
          var t = Type.GetType(
            "BlackMaple.FMSInsight.Niigata." + typeName + ", BlackMaple.FMSInsight.Niigata"
          );
          if (t != null)
            return t;
          t = Type.GetType("BlackMaple.MachineWatchInterface." + typeName + ", BlackMaple.MachineFramework")!;
          return t;
        }
        else
        {
          return Type.GetType(typeName + ", " + assemblyName)!;
        }
      }
    }

    public record LoadedPalletsDebugLine
    {
      public NiigataStatus? status;
    }

    public static (CellState, NiigataStationNames) LoadFromFMSInsightDir(string directory, string timestamp)
    {
      var lastDebugFile = Directory
        .GetFiles(directory, "fmsinsight-debug*.txt")
        .OrderByDescending(f => f)
        .First();

      NiigataStatus? niigataSt = null;
      using (var f = File.OpenText(lastDebugFile))
      {
        while (!f.EndOfStream)
        {
          var line = f.ReadLine();
          if (line != null && line.Contains($"\"@t\":\"{timestamp}\""))
          {
            niigataSt = JsonConvert
              .DeserializeObject<LoadedPalletsDebugLine>(
                line,
                new JsonSerializerSettings()
                {
                  TypeNameHandling = TypeNameHandling.All,
                  MetadataPropertyHandling = MetadataPropertyHandling.ReadAhead,
                  SerializationBinder = new NiigataNameConverter(),
                  Converters = new[] { new PalletLocConverter() }
                }
              )
              ?.status;
            break;
          }
        }
      }
      if (niigataSt == null)
      {
        throw new Exception($"Could not find debug file for {timestamp}");
      }

      string[]? machNames = null;
      string[]? reclampNames = null;
      foreach (var line in File.ReadAllLines(Path.Combine(directory, "config.ini")))
      {
        if (line.StartsWith("Machine Names"))
        {
          machNames = line.Split('=', 2)[1].Split(',').Select(s => s.Trim()).ToArray();
        }
        if (line.StartsWith("Reclamp Group Names"))
        {
          reclampNames = line.Split('=', 2)[1].Split(',').Select(s => s.Trim()).ToArray();
        }
      }
      var names = new NiigataStationNames()
      {
        ReclampGroupNames = new HashSet<string>(reclampNames ?? Enumerable.Empty<string>()),
        IccMachineToJobMachNames = (machNames ?? Enumerable.Empty<string>())
          .Select(
            (machName, idx) =>
            {
              var group = new string(machName.Reverse().SkipWhile(char.IsDigit).Reverse().ToArray());
              var num = int.Parse(machName.Substring(group.Length));
              return new
              {
                iccMc = idx + 1,
                group = group,
                num = num
              };
            }
          )
          .ToDictionary(x => x.iccMc, x => (group: x.group, num: x.num))
      };

      var repoCfg = RepositoryConfig.InitializeEventDatabase(
        new SerialSettings(),
        Path.Combine(directory, "niigatalog.db")
      );
      using var repo = repoCfg.OpenConnection();

      var convert = new CreateCellState(new FMSSettings(), names, null);
      var cellSt = convert.BuildCellState(repo, niigataSt);
      return (cellSt, names);
    }
  }
}
