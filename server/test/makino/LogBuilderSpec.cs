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
using BlackMaple.MachineFramework;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace BlackMaple.FMSInsight.Makino.Tests;

public sealed class LogBuilderSpec : IDisposable
{
  private RepositoryConfig _repo;
  private readonly IMakinoDB _makinoDB;

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
}
