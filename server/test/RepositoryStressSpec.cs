using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoFixture;
using BlackMaple.MachineFramework;
using Shouldly;

namespace BlackMaple.FMSInsight.Tests;

public sealed class RepositoryStressSpec : IDisposable
{
  private readonly string _tempDbFile;
  private readonly RepositoryConfig _repo;
  private readonly Fixture _fixture;

  public RepositoryStressSpec()
  {
    _tempDbFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".db");
    _repo = RepositoryConfig.InitializeEventDatabase(null, _tempDbFile);
    _fixture = new Fixture();
    _fixture.Customizations.Add(new ImmutableSpecimenBuilder());
    _fixture.Customizations.Add(new InjectNullValuesForNullableTypesSpecimenBuilder());
    _fixture.Customizations.Add(new DateOnlySpecimenBuilder());
  }

  public void Dispose()
  {
    _repo.Dispose();
    if (File.Exists(_tempDbFile))
    {
      File.Delete(_tempDbFile);
    }
  }

  [Test]
  public async Task ManualStress_ConcurrentStartupPollingAndWritesDoNotThrow()
  {
    if (Environment.GetEnvironmentVariable("FMSINSIGHT_RUN_STRESS_TESTS") != "1")
    {
      return;
    }

    SeedHistoryAndLogData();

    using var cts = CancellationTokenSource.CreateLinkedTokenSource(
      TestContext.Current.Execution.CancellationToken
    );
    cts.CancelAfter(TimeSpan.FromSeconds(15));

    var exceptions = new System.Collections.Concurrent.ConcurrentQueue<Exception>();

    var writerTask = Task.Run(
      async () =>
      {
        try
        {
          for (var idx = 0; idx < 60; idx++)
          {
            cts.Token.ThrowIfCancellationRequested();

            using var db = _repo.OpenConnection();
            db.RecordGeneralMessage(
              mats: [],
              program: "stress",
              result: "evt-" + idx.ToString(),
              pallet: 0,
              timeUTC: DateTime.UtcNow
            );

            await Task.Delay(TimeSpan.FromMilliseconds(10), cts.Token);
          }
        }
        catch (Exception ex)
        {
          exceptions.Enqueue(ex);
        }
      },
      cts.Token
    );

    var readerTasks = Enumerable
      .Range(0, 10)
      .Select(readerIdx =>
        Task.Run(
          async () =>
          {
            try
            {
              for (var iteration = 0; iteration < 25; iteration++)
              {
                cts.Token.ThrowIfCancellationRequested();

                using var db = _repo.OpenConnection();
                var recent = db.LoadRecentJobHistory(DateTime.UtcNow.AddDays(-30), alreadyKnownSchIds: []);
                recent.Jobs.ShouldNotBeNull();
                db.GetRecentLog(0).ToList();

                if (readerIdx % 2 == 0)
                {
                  db.LoadJobHistory(DateTime.UtcNow.AddDays(-30), DateTime.UtcNow.AddDays(1));
                }

                await Task.Delay(TimeSpan.FromMilliseconds(5), cts.Token);
              }
            }
            catch (Exception ex)
            {
              exceptions.Enqueue(ex);
            }
          },
          cts.Token
        )
      )
      .ToList();

    await Task.WhenAll(readerTasks.Append(writerTask));

    exceptions.ShouldBeEmpty();
  }

  private void SeedHistoryAndLogData()
  {
    using var db = _repo.OpenConnection();

    var now = DateTime.UtcNow;
    var jobs = ImmutableList.Create(RandJob("stress-1", now), RandJob("stress-2", now.AddMinutes(1)));
    db.AddJobs(
      new NewJobs() { ScheduleId = "stress-sch-1", Jobs = jobs },
      expectedPreviousScheduleId: null,
      addAsCopiedToSystem: true
    );

    var matId = db.AllocateMaterialID("stress-mat", "part", 1);
    db.RecordGeneralMessage(
      mats:
      [
        new EventLogMaterial()
        {
          MaterialID = matId,
          Process = 1,
          Face = 1,
        },
      ],
      program: "seed",
      result: "seed-result",
      pallet: 1,
      timeUTC: now
    );
  }

  private Job RandJob(string unique, DateTime startUtc)
  {
    var job = _fixture.Create<Job>();
    return job with
    {
      UniqueStr = unique,
      RouteStartUTC = startUtc,
      RouteEndUTC = startUtc.AddHours(1),
      Cycles = 5,
      Archived = false,
      Processes = job
        .Processes.Select(
          (proc, procIdx) =>
            new ProcessInfo()
            {
              Paths = proc
                .Paths.Select(path => path with { Casting = procIdx == 0 ? path.Casting : null })
                .ToImmutableList(),
            }
        )
        .ToImmutableList(),
    };
  }
}
