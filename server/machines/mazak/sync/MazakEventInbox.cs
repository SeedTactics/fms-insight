#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using BlackMaple.MachineFramework;

namespace MazakMachineInterface;

// One native CSV stream per repository, matching the established LG cursor's ownership.
// Payloads retain sealed L/U groups independently of the manufacturing log's foreign-ID maximum.
internal static class MazakEventInbox
{
  internal const string Stream = "mazak:csv:v1";

  internal sealed record Evidence
  {
    public int Version { get; init; } = 1;
    public required ImmutableList<LogEntry> Events { get; init; }
    public required ImmutableSortedSet<string> Dependencies { get; init; }
    public required ImmutableList<MazakEventRouteIdentity> Routes { get; init; }
    public required bool SelectedWorkObserved { get; init; }
    public required bool UnverifiedMaterialIdentityObserved { get; init; }
  }

  internal sealed record Pending(ControllerEventBatch Batch, Evidence Evidence)
  {
    internal LogTranslation.LogChunk Chunk =>
      Evidence.Events[0].Code is LogCode.LoadEnd or LogCode.UnloadEnd
        ? new()
        {
          LulEndChunk = Evidence.Events,
          PalletForFinalLULEvents =
            Batch.State == ControllerEventBatchState.Open ? Evidence.Events[0].Pallet : null,
        }
        : new() { NonLulEndEvt = Evidence.Events.Single() };
  }

  internal static ImmutableList<LogEntry> Events(LogTranslation.LogChunk chunk) =>
    chunk.LulEndChunk?.ToImmutableList() ?? [chunk.NonLulEndEvt];

  internal static ImmutableList<Pending> Load(IRepository repository) =>
    repository
      .LoadPendingControllerEventBatches(Stream)
      .Select(batch =>
      {
        var evidence =
          JsonSerializer.Deserialize<Evidence>(batch.Payload)
          ?? throw new InvalidOperationException("Missing saved Mazak evidence.");
        if (
          evidence.Version != 1
          || evidence.Events.IsEmpty
          || evidence.Events[0].ForeignID != batch.BatchId
          || evidence.Events[^1].ForeignID != batch.LastEventId
        )
          throw new InvalidOperationException("Unsupported or inconsistent saved Mazak evidence.");
        return new Pending(
          batch,
          evidence with
          {
            Dependencies = evidence.Dependencies.ToImmutableSortedSet(StringComparer.Ordinal),
          }
        );
      })
      .ToImmutableList();

  internal static bool Receive(
    IRepository repository,
    MazakAllDataAndLogs data,
    MazakConfig configuration,
    FMSSettings settings,
    string legacyWatermark
  )
  {
    var received = repository.ControllerEventReceiptWatermark(Stream);
    var after = string.IsNullOrEmpty(received) ? legacyWatermark : received;
    var incoming = data
      .Logs.Where(e => StringComparer.Ordinal.Compare(e.ForeignID, after) > 0)
      .OrderBy(e => e.ForeignID, StringComparer.Ordinal)
      .ToImmutableList();
    if (incoming.IsEmpty)
      return false;
    var tail = Load(repository)
      .SingleOrDefault(p => p.Batch.State == ControllerEventBatchState.Open);
    var raw = (tail?.Evidence.Events ?? []).AddRange(incoming);
    var batches = LogTranslation
      .ChunkLulEvents(raw)
      .Select(chunk =>
      {
        var events = Events(chunk);
        var isTail = events[0].ForeignID == tail?.Batch.BatchId;
        var newlyObserved = isTail
          ? events
            .Where(e => StringComparer.Ordinal.Compare(e.ForeignID, tail!.Batch.LastEventId) > 0)
            .ToImmutableList()
          : events;
        // A separator can seal the previous open group without changing its immutable payload.
        var evidence =
          isTail && events.SequenceEqual(tail!.Evidence.Events)
            ? tail.Evidence
            : new Evidence
            {
              Events = events,
              Dependencies = MazakEventDependencies
                .Read(repository, data, configuration, settings, events)
                .Union(isTail ? tail!.Evidence.Dependencies : [])
                .ToImmutableSortedSet(StringComparer.Ordinal),
              Routes = (isTail ? tail!.Evidence.Routes : [])
                .AddRange(
                  MazakEventDependencies.CaptureVerifiedRoutes(repository, data, newlyObserved)
                )
                .Distinct(MazakEventDependencies.RouteIdentityComparer)
                .ToImmutableList(),
              SelectedWorkObserved =
                isTail && tail!.Evidence.SelectedWorkObserved
                || MazakEventDependencies.HasSelectedWork(
                  repository,
                  data,
                  configuration,
                  newlyObserved
                ),
              UnverifiedMaterialIdentityObserved =
                isTail && tail!.Evidence.UnverifiedMaterialIdentityObserved
                || MazakEventDependencies.HasUnverifiedMaterialIdentity(
                  repository,
                  data,
                  newlyObserved
                ),
            };
        return new ControllerEventBatch
        {
          BatchId = events[0].ForeignID,
          LastEventId = events[^1].ForeignID,
          Payload = JsonSerializer.Serialize(evidence),
          State = chunk.PalletForFinalLULEvents.HasValue
            ? ControllerEventBatchState.Open
            : ControllerEventBatchState.Received,
        };
      })
      .ToImmutableList();
    repository.ReceiveControllerEventBatches(Stream, batches);
    return true;
  }

  internal static MazakResourceProtection ResourceProtection(
    IRepository repository,
    MazakAllData data
  )
  {
    var pending = Load(repository);
    if (pending.IsEmpty)
      return MazakResourceProtection.Empty;
    var events = pending.SelectMany(p => p.Evidence.Events).ToImmutableList();
    var parts = events
      .Where(e => !string.IsNullOrEmpty(e.FullPartName))
      .Select(e => e.FullPartName)
      .ToImmutableHashSet(StringComparer.Ordinal);
    var pallets = events.Where(e => e.Pallet > 0).Select(e => e.Pallet).ToImmutableHashSet();
    return new MazakResourceProtection
    {
      PartNames = parts,
      PalletNumbers = pallets,
      JobUniques = pending
        .SelectMany(p => p.Evidence.Dependencies)
        .Where(scope => scope.StartsWith("job:", StringComparison.Ordinal))
        .Select(scope => scope[4..])
        .ToImmutableHashSet(StringComparer.Ordinal),
      ProgramNames = events
        .Where(e => !string.IsNullOrEmpty(e.Program))
        .Select(e => e.Program)
        .Concat(pending.SelectMany(p => p.Evidence.Routes).SelectMany(r => r.NativeProgramNames))
        .ToImmutableHashSet(StringComparer.Ordinal),
      FixtureNames = pending
        .SelectMany(p => p.Evidence.Routes)
        .SelectMany(r => r.NativeFixtureNames)
        .ToImmutableHashSet(StringComparer.Ordinal),
      ScheduleIds = data
        .Schedules.Where(s => parts.Contains(s.PartName))
        .Select(s => s.Id)
        .Concat(
          data.PalletSubStatuses.Where(s => pallets.Contains(s.PalletNumber))
            .Select(s => s.ScheduleID)
        )
        .Concat(pending.SelectMany(p => p.Evidence.Routes).Select(r => r.NativeScheduleId))
        .ToImmutableHashSet(),
      SuspendCleanup = pending.Any(p =>
        p.Evidence.Dependencies.Contains(MazakEventDependencies.Unknown)
      ),
    };
  }
}
