/* Copyright (c) 2024, John Lenz

All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

    * Redistributions of source code must retain the above copyright
      notice, this list of conditions and the following disclaimer.

    * Redistributions in binary form must reproduce the above
      copyright notice, this list of conditions and the following
      disclaimer in the documentation and/or other materials provided
      with the distribution.

    * Neither the name of John Lenz, Black Maple Software, SeedTactics,
      nor the names of other contributors may be used to endorse or
      promote products derived from this software without specific
      prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using AutoFixture;
using BlackMaple.MachineFramework;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

#nullable enable

namespace BlackMaple.FMSInsight.Makino.Tests;

public sealed class LogBuilderSpec : IDisposable
{
  private readonly RepositoryConfig _repo;
  private readonly IMakinoDB _makinoDB;
  private readonly Fixture _fixture = new();
  private readonly List<LogEntry> _expectedLog = [];

  public LogBuilderSpec()
  {
    var serialSettings = new SerialSettings()
    {
      SerialType = SerialType.AssignOneSerialPerMaterial,
      ConvertMaterialIDToSerial = (m) => SerialSettings.ConvertToBase62(m, 10)
    };
    _repo = RepositoryConfig.InitializeMemoryDB(serialSettings);

    _makinoDB = Substitute.For<IMakinoDB>();
    _makinoDB.LoadCurrentInfo(null).ThrowsForAnyArgs(new Exception("Should not be called"));
    _makinoDB
      .QueryLoadUnloadResults(default, default)
      .ThrowsForAnyArgs(new Exception("Query Load not configured"));
    _makinoDB
      .QueryMachineResults(default, default)
      .ThrowsForAnyArgs(new Exception("Query machine not configured"));
    _makinoDB
      .QueryCommonValues(default)
      .ThrowsForAnyArgs(new Exception("Query common values not configured"));

    _makinoDB
      .Devices()
      .Returns(
        new Dictionary<int, PalletLocation>()
        {
          {
            1,
            new PalletLocation()
            {
              Location = PalletLocationEnum.LoadUnload,
              Num = 1,
              StationGroup = "L/U"
            }
          },
          {
            2,
            new PalletLocation()
            {
              Location = PalletLocationEnum.LoadUnload,
              Num = 2,
              StationGroup = "L/U"
            }
          },
          {
            3,
            new PalletLocation()
            {
              Location = PalletLocationEnum.Machine,
              Num = 1,
              StationGroup = "Mach"
            }
          },
          {
            4,
            new PalletLocation()
            {
              Location = PalletLocationEnum.Machine,
              Num = 2,
              StationGroup = "Mach"
            }
          },
        }
      );
  }

  void IDisposable.Dispose()
  {
    _repo.CloseMemoryConnection();
  }

  private record TestMat
  {
    public required string OrderName { get; init; }
    public required string PartName { get; init; }
    public required string Revision { get; init; }
    public required int Process { get; init; }

    public required int Quantity { get; init; }

    public required int PalletID { get; init; }
    public required int FixtureNum { get; init; }
    public required string FixtureName { get; init; }
    public required string FixtureComment { get; init; }

    public required long StartingMatID { get; init; }
  }

  private TestMat MkMat(int palId, int fixNum, long matId)
  {
    return new TestMat()
    {
      OrderName = "order" + _fixture.Create<string>(),
      PartName = "part" + _fixture.Create<string>(),
      Revision = "rev" + _fixture.Create<string>(),
      Process = 1,
      Quantity = 1,
      PalletID = palId,
      FixtureNum = fixNum,
      FixtureName = "fix" + _fixture.Create<string>(),
      FixtureComment = "comment" + _fixture.Create<string>(),
      StartingMatID = matId
    };
  }

  private MachineResults Mach(
    DateTime start,
    int elapsedMin,
    int device,
    TestMat mat,
    string program,
    int spindleTime
  )
  {
    var machr = new MachineResults()
    {
      StartDateTimeLocal = start.ToLocalTime(),
      EndDateTimeLocal = start.ToLocalTime() + TimeSpan.FromMinutes(elapsedMin),
      StartDateTimeUTC = start.ToUniversalTime(),
      EndDateTimeUTC = start.ToUniversalTime() + TimeSpan.FromMinutes(elapsedMin),
      DeviceID = device,
      PalletID = mat.PalletID,
      FixtureNumber = mat.FixtureNum,
      FixtureName = mat.FixtureName,
      FixtureComment = mat.FixtureComment,
      OrderName = mat.OrderName,
      PartName = mat.PartName,
      Revision = mat.Revision,
      ProcessNum = mat.Process,
      JobNum = 1,
      ProcessName = "unused process name",
      Program = program,
      SpindleTimeSeconds = spindleTime,
      OperQuantities = mat.Quantity == 1 ? [1] : [1, mat.Quantity - 1]
    };

    _expectedLog.Add(
      new LogEntry()
      {
        Counter = 0,
        Material = Enumerable
          .Range(0, mat.Quantity)
          .Select(i => new LogMaterial()
          {
            MaterialID = mat.StartingMatID + i,
            JobUniqueStr = mat.OrderName,
            Serial = SerialSettings.ConvertToBase62(mat.StartingMatID + i, 10),
            Workorder = "",
            NumProcesses = 1,
            Face = 1,
            PartName = mat.PartName,
            Process = mat.Process,
            Path = null
          })
          .ToImmutableList(),
        LogType = LogType.MachineCycle,
        StartOfCycle = false,
        EndTimeUTC = start.ToUniversalTime() + TimeSpan.FromMinutes(elapsedMin),
        LocationName = "MC",
        LocationNum = device == 3 ? 1 : 2,
        Pallet = mat.PalletID,
        Program = program,
        Result = "",
        ElapsedTime = TimeSpan.FromMinutes(elapsedMin),
        ActiveOperationTime = TimeSpan.FromSeconds(spindleTime),
      }
    );

    return machr;
  }

  private WorkSetResults Load(
    DateTime start,
    int elapsedMin,
    int device,
    TestMat? loadMat,
    TestMat? unloadMat,
    int palCycleMin
  )
  {
    var workr = new WorkSetResults()
    {
      StartDateTimeUTC = start.ToUniversalTime(),
      EndDateTimeUTC = start.ToUniversalTime() + TimeSpan.FromMinutes(elapsedMin),
      DeviceID = device,
      PalletID = loadMat?.PalletID ?? unloadMat?.PalletID ?? 0,
      FixtureNumber = loadMat?.FixtureNum ?? unloadMat?.FixtureNum ?? 0,
      FixtureName = loadMat?.FixtureName ?? unloadMat?.FixtureName ?? "",
      FixtureComment = loadMat?.FixtureComment ?? unloadMat?.FixtureComment ?? "",
      UnloadOrderName = unloadMat?.OrderName,
      LoadOrderName = loadMat?.OrderName,
      UnloadPartName = unloadMat?.PartName,
      UnloadRevision = unloadMat?.Revision,
      UnloadProcessNum = unloadMat?.Process ?? 0,
      UnloadJobNum = 3,
      LoadPartName = loadMat?.PartName,
      LoadRevision = loadMat?.Revision,
      LoadProcessNum = loadMat?.Process ?? 0,
      LoadJobNum = 1,
      UnloadProcessName = "unused unload process name",
      LoadProcessName = "unused load process name",
      LoadQuantities =
        loadMat == null
          ? [0]
          : loadMat.Quantity == 1
            ? [1]
            : [1, loadMat.Quantity - 1],
      UnloadNormalQuantities =
        unloadMat == null
          ? [0]
          : unloadMat.Quantity == 1
            ? [1]
            : [1, unloadMat.Quantity - 1],
      UnloadScrapQuantities = [],
      UnloadOutProcQuantities = [],
      Remachine = false
    };

    foreach (var mat in new[] { loadMat, unloadMat })
    {
      if (mat == null)
        continue;
      _expectedLog.Add(
        new LogEntry()
        {
          Counter = 0,
          Material = Enumerable
            .Range(0, mat.Quantity)
            .Select(i => new LogMaterial()
            {
              MaterialID = mat.StartingMatID + i,
              JobUniqueStr = mat.OrderName,
              Serial = SerialSettings.ConvertToBase62(mat.StartingMatID + i, 10),
              Workorder = "",
              NumProcesses = 1,
              Face = 1,
              PartName = mat.PartName,
              Process = mat.Process,
              Path = null
            })
            .ToImmutableList(),
          LogType = LogType.LoadUnloadCycle,
          StartOfCycle = false,
          EndTimeUTC =
            start.ToUniversalTime()
            + TimeSpan.FromMinutes(elapsedMin)
            + (mat == loadMat ? TimeSpan.FromSeconds(1) : TimeSpan.Zero),
          LocationName = "L/U",
          LocationNum = device,
          Pallet = loadMat?.PalletID ?? unloadMat!.PalletID,
          Program = loadMat == mat ? "LOAD" : "UNLOAD",
          Result = loadMat == mat ? "LOAD" : "UNLOAD",
          ElapsedTime = TimeSpan.FromMinutes(elapsedMin) / 2,
          ActiveOperationTime = TimeSpan.FromMinutes(elapsedMin) / 2,
        }
      );
    }

    if (loadMat != null)
    {
      for (int i = 0; i < loadMat.Quantity; i++)
      {
        _expectedLog.Add(
          new LogEntry()
          {
            Counter = 0,
            Material =
            [
              new LogMaterial()
              {
                MaterialID = loadMat.StartingMatID + i,
                JobUniqueStr = loadMat.OrderName,
                Serial = SerialSettings.ConvertToBase62(loadMat.StartingMatID + i, 10),
                Workorder = "",
                NumProcesses = 1,
                Face = 0,
                PartName = loadMat.PartName,
                Process = 0,
                Path = null
              }
            ],
            LogType = LogType.PartMark,
            StartOfCycle = false,
            EndTimeUTC = start.ToUniversalTime() + TimeSpan.FromMinutes(elapsedMin) + TimeSpan.FromSeconds(1),
            LocationName = "Mark",
            LocationNum = 1,
            Pallet = 0,
            Program = "MARK",
            Result = SerialSettings.ConvertToBase62(loadMat.StartingMatID + i, 10),
            ElapsedTime = TimeSpan.FromMinutes(-1),
            ActiveOperationTime = TimeSpan.Zero
          }
        );
      }
    }

    _expectedLog.Add(
      new LogEntry()
      {
        Counter = 0,
        Material = [],
        LogType = LogType.PalletCycle,
        StartOfCycle = false,
        EndTimeUTC = start.ToUniversalTime() + TimeSpan.FromMinutes(elapsedMin),
        LocationName = "Pallet Cycle",
        LocationNum = 1,
        Pallet = loadMat?.PalletID ?? unloadMat!.PalletID,
        Program = "",
        Result = "PalletCycle",
        ElapsedTime = TimeSpan.FromMinutes(palCycleMin),
        ActiveOperationTime = TimeSpan.Zero,
      }
    );

    return workr;
  }

  [Fact]
  public void NoLogOnEmpty()
  {
    using var db = _repo.OpenConnection();

    var lastDate = DateTime.UtcNow.AddDays(-30);

    _makinoDB
      .QueryLoadUnloadResults(
        lastDate,
        Arg.Is<DateTime>(x =>
          Math.Abs(x.Subtract(DateTime.UtcNow.AddMinutes(1)).Ticks) < TimeSpan.FromSeconds(2).Ticks
        )
      )
      .Returns([]);
    _makinoDB
      .QueryMachineResults(
        lastDate,
        Arg.Is<DateTime>(x =>
          Math.Abs(x.Subtract(DateTime.UtcNow.AddMinutes(1)).Ticks) < TimeSpan.FromSeconds(2).Ticks
        )
      )
      .Returns([]);

    new LogBuilder(_makinoDB, db).CheckLogs(lastDate).Should().BeFalse();

    db.MaxLogDate().Should().Be(DateTime.MinValue);
  }

  [Fact]
  public void SingleCycle()
  {
    var mat = MkMat(palId: 2, fixNum: 4, matId: 1);

    var now = DateTime.UtcNow;
    var start = now.AddHours(-2);

    _makinoDB
      .QueryLoadUnloadResults(now.AddDays(-30), Arg.Any<DateTime>())
      .Returns(
        [
          Load(start, elapsedMin: 10, device: 1, loadMat: mat, unloadMat: null, palCycleMin: 0),
          Load(start.AddMinutes(30), elapsedMin: 5, device: 2, loadMat: null, unloadMat: mat, palCycleMin: 25)
        ]
      );

    List<MachineResults> machs =
    [
      Mach(start.AddMinutes(15), elapsedMin: 11, device: 3, mat: mat, program: "prog1", spindleTime: 5)
    ];

    _makinoDB.QueryMachineResults(now.AddDays(-30), Arg.Any<DateTime>()).Returns(machs);

    _makinoDB.QueryCommonValues(Arg.Is(machs[0])).Returns([]);

    using var db = _repo.OpenConnection();

    new LogBuilder(_makinoDB, db).CheckLogs(now.AddDays(-30)).Should().BeTrue();

    var log = db.GetLogEntries(start, now);
    _expectedLog.Sort((a, b) => a.EndTimeUTC.CompareTo(b.EndTimeUTC));

    log.Should().BeEquivalentTo(_expectedLog, options => options.Excluding(x => x.Counter));
  }
}
