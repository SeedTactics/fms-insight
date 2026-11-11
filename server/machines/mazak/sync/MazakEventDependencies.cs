using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using BlackMaple.MachineFramework;

namespace MazakMachineInterface;

// An observed definition identity guards a received event across retries. It is not positive proof
// for pre-upgrade history and does not establish durable controller provenance.
public sealed record MazakEventRouteIdentity
{
  public required string FullPartName { get; init; }
  public required int NativeProcess { get; init; }
  public required int NativeScheduleId { get; init; }
  public required string JobUnique { get; init; }
  public required string JobPartName { get; init; }
  public required int JobProcess { get; init; }
  public required int Path { get; init; }
  public required string DefinitionJson { get; init; }
  public required ImmutableList<string> NativeProgramNames { get; init; }
  public required ImmutableList<string> NativeFixtureNames { get; init; }
}

// Dependencies constrain replay ordering; they do not establish an event's manufacturing owner.
// Recalculate from current repository facts before deciding whether a later batch can proceed.
public static class MazakEventDependencies
{
  public const string Unknown = "*";

  public static bool SameIdentity(MazakEventRouteIdentity left, MazakEventRouteIdentity right) =>
    ReferenceEquals(left, right)
    || left is not null
      && right is not null
      && left.FullPartName == right.FullPartName
      && left.NativeProcess == right.NativeProcess
      && left.NativeScheduleId == right.NativeScheduleId
      && left.JobUnique == right.JobUnique
      && left.JobPartName == right.JobPartName
      && left.JobProcess == right.JobProcess
      && left.Path == right.Path
      && left.DefinitionJson == right.DefinitionJson
      && left.NativeProgramNames.SequenceEqual(right.NativeProgramNames, StringComparer.Ordinal)
      && left.NativeFixtureNames.SequenceEqual(right.NativeFixtureNames, StringComparer.Ordinal);

  public static IEqualityComparer<MazakEventRouteIdentity> RouteIdentityComparer { get; } =
    EqualityComparer<MazakEventRouteIdentity>.Create(
      SameIdentity,
      identity =>
        identity is null
          ? 0
          : HashCode.Combine(
            identity.FullPartName,
            identity.NativeProcess,
            identity.NativeScheduleId,
            identity.JobUnique,
            identity.JobPartName,
            identity.JobProcess,
            identity.Path,
            identity.DefinitionJson
          )
    );

  public static string Pallet(int rawPallet) =>
    "pallet:" + rawPallet.ToString(CultureInfo.InvariantCulture);

  public static string Part(string fullPartName) => "part:" + fullPartName;

  public static string Job(string unique) => "job:" + unique;

  public static string Queue(string queue) => "queue:" + queue;

  public static string Material(long id) => "material:" + id.ToString(CultureInfo.InvariantCulture);

  public static bool Conflicts(IReadOnlySet<string> left, IReadOnlySet<string> right) =>
    left.Count > 0
    && right.Count > 0
    && (left.Contains(Unknown) || right.Contains(Unknown) || left.Overlaps(right));

  // Classification is deliberately conservative and does not infer the event's owning job.
  public static bool HasSelectedWork(
    IRepository repository,
    MazakAllData data,
    MazakConfig configuration,
    ImmutableList<LogEntry> events
  ) =>
    configuration.UseInsightJobQuantities is not null
    && events.Any(entry =>
      IsMaterialEvent(entry.Code)
      && HasSelectedWork(
        repository,
        data,
        configuration,
        entry,
        VerifiedRoute(repository, data, entry)
      )
    );

  public static ImmutableList<MazakEventRouteIdentity> CaptureVerifiedRoutes(
    IRepository repository,
    MazakAllData data,
    ImmutableList<LogEntry> events
  )
  {
    var identities = ImmutableList.CreateBuilder<MazakEventRouteIdentity>();
    foreach (var entry in events.Where(entry => IsMaterialEvent(entry.Code)))
    {
      if (VerifiedRoute(repository, data, entry) is not { } route)
        continue;
      var part = data.Parts.Single(row => row.PartName == entry.FullPartName);
      var schedule = data.Schedules.Single(row => row.PartName == entry.FullPartName);
      identities.Add(
        new MazakEventRouteIdentity
        {
          FullPartName = entry.FullPartName,
          NativeProcess = entry.Process,
          NativeScheduleId = schedule.Id,
          JobUnique = route.Job.UniqueStr,
          JobPartName = route.Job.PartName,
          JobProcess = route.Process,
          Path = route.Path,
          NativeProgramNames = part
            .Processes.Select(process => process.MainProgram)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToImmutableList(),
          NativeFixtureNames = part
            .Processes.Select(process => process.Fixture)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToImmutableList(),
          DefinitionJson = JsonSerializer.Serialize(
            new
            {
              ScheduleComment = schedule.Comment,
              part.PartName,
              part.Comment,
              part.TotalProcess,
              part.MaterialName,
              part.Part_1,
              part.Part_2,
              part.Part_3,
              part.Part_4,
              part.Part_5,
              Processes = part
                .Processes.OrderBy(process => process.ProcessNumber)
                .ToImmutableList(),
              Programs = (data.MainPrograms ?? [])
                .Where(program =>
                  part.Processes.Any(process => process.MainProgram == program.MainProgram)
                )
                .OrderBy(program => program.MainProgram, StringComparer.Ordinal)
                .ThenBy(program => program.Comment, StringComparer.Ordinal)
                .ToImmutableList(),
              Fixtures = (data.Fixtures ?? [])
                .Where(fixture =>
                  part.Processes.Any(process => process.Fixture == fixture.FixtureName)
                )
                .OrderBy(fixture => fixture.FixtureName, StringComparer.Ordinal)
                .ThenBy(fixture => fixture.Comment, StringComparer.Ordinal)
                .ToImmutableList(),
            }
          ),
        }
      );
    }
    return identities
      .Distinct(RouteIdentityComparer)
      .OrderBy(identity => identity.FullPartName, StringComparer.Ordinal)
      .ThenBy(identity => identity.NativeProcess)
      .ToImmutableList();
  }

  // Persist this observation monotonically when appending an open batch. A later event's
  // matching route must never fill the earlier event's missing receipt identity.
  public static bool HasUnverifiedMaterialIdentity(
    IRepository repository,
    MazakAllData data,
    ImmutableList<LogEntry> events
  ) =>
    events.Any(entry =>
      IsMaterialEvent(entry.Code) && VerifiedRoute(repository, data, entry) is null
    );

#nullable enable
  public static string? DeferredReasonFromReceipt(
    IRepository repository,
    MazakAllData data,
    MazakConfig configuration,
    ImmutableList<LogEntry> events,
    ImmutableList<MazakEventRouteIdentity> receivedRoutes,
    bool selectedWorkObserved
  )
  {
    if (!selectedWorkObserved && !HasSelectedWork(repository, data, configuration, events))
      return null;
    var currentRoutes = CaptureVerifiedRoutes(repository, data, events);
    foreach (var entry in events.Where(entry => IsMaterialEvent(entry.Code)))
    {
      var received = receivedRoutes
        .Where(identity =>
          identity.FullPartName == entry.FullPartName && identity.NativeProcess == entry.Process
        )
        .ToImmutableList();
      if (received.Count != 1)
        return $"Insight-owned event for pallet {entry.Pallet}, part {entry.FullPartName} was received without verified native ownership. A current matching row does not establish its historical identity; reconcile the retained event.";
      var current = currentRoutes
        .Where(identity =>
          identity.FullPartName == entry.FullPartName && identity.NativeProcess == entry.Process
        )
        .ToImmutableList();
      if (current.Count != 1)
        return $"Waiting for the verified native route recorded with the Insight-owned event for pallet {entry.Pallet}, part {entry.FullPartName}.";
      if (!SameIdentity(received[0], current[0]))
        return $"Native route ownership or definition changed after the Insight-owned event for pallet {entry.Pallet}, part {entry.FullPartName} was received. Reconcile its retained original identity before applying it.";
    }
    return DeferredReason(repository, data, configuration, events);
  }

  // This is current eligibility, not durable provenance or evidence that a missing row was repaired.
  public static string? DeferredReason(
    IRepository repository,
    MazakAllData data,
    MazakConfig configuration,
    ImmutableList<LogEntry> events
  )
  {
    if (configuration.UseInsightJobQuantities is null)
      return null;
    foreach (var entry in events.Where(entry => IsMaterialEvent(entry.Code)))
    {
      var route = VerifiedRoute(repository, data, entry);
      if (!HasSelectedWork(repository, data, configuration, entry, route))
        continue;
      if (route is null)
        return $"Waiting for an unambiguous native route for Insight-owned work on pallet {entry.Pallet}, part {entry.FullPartName}. Recorded material and events remain retained.";
      if (!configuration.UsesInsightJobQuantities(route.Value.Job))
        return $"Native route ownership conflicts with recorded Insight-owned work on pallet {entry.Pallet}, part {entry.FullPartName}. Reconcile ownership before applying this event.";
    }
    return null;
  }

#nullable restore

  private static bool IsMaterialEvent(LogCode code) =>
    code
      is LogCode.LoadBegin
        or LogCode.LoadEnd
        or LogCode.UnloadBegin
        or LogCode.UnloadEnd
        or LogCode.MachineCycleStart
        or LogCode.MachineCycleEnd;

  private static bool HasSelectedWork(
    IRepository repository,
    MazakAllData data,
    MazakConfig configuration,
    LogEntry entry,
    (BlackMaple.MachineFramework.Job Job, int Process, int Path)? route
  )
  {
    var comments = data
      .Schedules.Where(row => row.PartName == entry.FullPartName)
      .Select(row => row.Comment)
      .Concat(
        data.Parts.Where(row => row.PartName == entry.FullPartName).Select(row => row.Comment)
      )
      .Distinct(StringComparer.Ordinal);
    foreach (var comment in comments)
      if (
        MazakPart.IsSailPart(entry.FullPartName, comment)
        && ReadComment(comment) is { } parsed
        && configuration.UsesInsightJobQuantities(repository.LoadJob(parsed.Comment.Unique))
      )
        return true;

    if (entry.Pallet > 0)
    {
      var ids = repository
        .CurrentPalletLog(configuration.TranslatePalletNumber(entry.Pallet))
        .SelectMany(log => log.Material)
        .Where(mat => mat.MaterialID >= 0)
        .Select(mat => mat.MaterialID)
        .ToImmutableHashSet();
      foreach (var id in ids)
        if (
          repository.GetMaterialDetails(id) is { JobUnique.Length: > 0 } material
          && configuration.UsesInsightJobQuantities(repository.LoadJob(material.JobUnique))
        )
          return true;
    }

    // A verified ordinary route wins over a base-name resemblance. Without a route, matching
    // accepted selected demand is enough to require review, never enough to assign ownership.
    if (route is { } verified)
      return configuration.UsesInsightJobQuantities(verified.Job);
    var basePart = string.IsNullOrEmpty(entry.FullPartName)
      ? ""
      : MazakPart.ExtractPartNameFromMazakPartName(entry.FullPartName);
    return repository
      .LoadUnarchivedJobs()
      .Any(job =>
        configuration.UsesInsightJobQuantities(job)
        && (
          !string.IsNullOrEmpty(basePart) && job.PartName == basePart
          || !string.IsNullOrEmpty(entry.JobPartName) && job.PartName == entry.JobPartName
        )
      );
  }

  public static ImmutableHashSet<string> Read(
    IRepository repository,
    MazakAllData data,
    MazakConfig configuration,
    FMSSettings settings,
    ImmutableList<LogEntry> events
  )
  {
    var scopes = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
    if (events.IsEmpty)
      scopes.Add(Unknown);
    foreach (var entry in events)
    {
      if (entry.Pallet <= 0)
        scopes.Add(Unknown);
      else
        scopes.Add(Pallet(entry.Pallet));

      // Movement records retain the pallet's existing material rather than selecting a new set.
      if (
        entry.Code
        is LogCode.PalletMoving
          or LogCode.PalletMoveComplete
          or LogCode.StartRotatePalletIntoMachine
          or LogCode.StartRotatePalletOutOfMachine
      )
        continue;

      if (
        entry.Code
        is not (
          LogCode.LoadBegin
          or LogCode.LoadEnd
          or LogCode.UnloadBegin
          or LogCode.UnloadEnd
          or LogCode.MachineCycleStart
          or LogCode.MachineCycleEnd
        )
      )
        scopes.Add(Unknown);

      if (string.IsNullOrEmpty(entry.FullPartName))
        scopes.Add(Unknown);
      else
        scopes.Add(Part(entry.FullPartName));

      var route = VerifiedRoute(repository, data, entry);
      if (route is not { } verified)
        scopes.Add(Unknown);
      else
      {
        scopes.Add(Job(verified.Job.UniqueStr));
        AddQueues(scopes, verified.Job, verified.Process);
        if (
          entry.Code is LogCode.UnloadBegin or LogCode.UnloadEnd
          && verified.Job.Processes[verified.Process - 1].BasketUnloadStations?.Count > 0
        )
          AddQueue(scopes, settings.QuarantineQueue);
      }

      if (entry.Pallet <= 0)
        continue;
      var history = repository.CurrentPalletLog(configuration.TranslatePalletNumber(entry.Pallet));
      var material = history
        .SelectMany(log => log.Material)
        .Where(mat => mat.MaterialID >= 0)
        .DistinctBy(mat => (mat.MaterialID, mat.Process))
        .ToImmutableList();
      foreach (var mat in material)
      {
        scopes.Add(Material(mat.MaterialID));
        var details = repository.GetMaterialDetails(mat.MaterialID);
        if (
          details is not { JobUnique.Length: > 0 }
          || repository.LoadJob(details.JobUnique) is not { } job
        )
        {
          scopes.Add(Unknown);
          continue;
        }
        scopes.Add(Job(job.UniqueStr));
        if (mat.Process <= 0 || mat.Process > job.Processes.Count)
          scopes.Add(Unknown);
        else
          AddQueues(scopes, job, mat.Process);
      }

      if (entry.Code is LogCode.UnloadBegin or LogCode.UnloadEnd)
        foreach (var signal in history.Where(log => log.LogType == LogType.SignalQuarantine))
          AddQueue(
            scopes,
            string.IsNullOrEmpty(signal.LocationName)
              ? settings.QuarantineQueue
              : signal.LocationName
          );

      // Loading may allocate new IDs. Other material events require an established identity;
      // ordinary translation can manufacture fallback IDs when tracking is missing.
      if (
        entry.Code is not (LogCode.LoadBegin or LogCode.LoadEnd)
        && (
          route is not { } known
          || !material.Any(mat =>
            mat.JobUniqueStr == known.Job.UniqueStr && mat.Process == known.Process
          )
        )
      )
        scopes.Add(Unknown);
    }
    return scopes.ToImmutable();
  }

  private static (BlackMaple.MachineFramework.Job Job, int Process, int Path)? VerifiedRoute(
    IRepository repository,
    MazakAllData data,
    LogEntry entry
  )
  {
    if (string.IsNullOrEmpty(entry.FullPartName) || entry.Process <= 0)
      return null;
    var schedules = data
      .Schedules.Where(row => row.PartName == entry.FullPartName)
      .ToImmutableList();
    var parts = data.Parts.Where(row => row.PartName == entry.FullPartName).ToImmutableList();
    if (
      schedules.Count != 1
      || parts.Count != 1
      || !MazakPart.IsSailPart(entry.FullPartName, schedules[0].Comment)
      || schedules[0].Comment != parts[0].Comment
      || schedules[0].Processes.Count(process => process.ProcessNumber == entry.Process) != 1
      || parts[0].Processes.Count(process => process.ProcessNumber == entry.Process) != 1
    )
      return null;
    if (ReadComment(schedules[0].Comment) is not { } parsed)
      return null;
    var comment = parsed.Comment;
    if (
      !comment.HasUnique
      || repository.LoadJob(comment.Unique) is not { } job
      || MazakPart.ExtractPartNameFromMazakPartName(entry.FullPartName) != job.PartName
      || !string.IsNullOrEmpty(entry.JobPartName) && entry.JobPartName != job.PartName
    )
      return null;
    var process = comment.JobProcessForMazakProcess(entry.Process);
    if (process <= 0 || process > job.Processes.Count)
      return null;
    return parsed.Path > 0 && parsed.Path <= job.Processes[process - 1].Paths.Count
      ? (job, process, parsed.Path)
      : null;
  }

  private static (MazakPart.MazakCommentInfo Comment, int Path)? ReadComment(string nativeComment)
  {
    var comment = MazakPart.ParseCommentInfo(nativeComment);
    if (!comment.HasUnique)
      return null;
    if (!comment.IsSplit)
      return (comment, 1);
    // The shared comment parser validated both positive numbers; its result exposes process
    // but not path, so recover only the path here without repeating the ownership grammar.
    var suffix = nativeComment.Length - "-InsightS".Length;
    var pathSeparator = nativeComment.LastIndexOf('-', suffix - 1);
    var path = int.Parse(
      nativeComment.AsSpan(pathSeparator + 1, suffix - pathSeparator - 1),
      NumberStyles.None,
      CultureInfo.InvariantCulture
    );
    return (comment, path);
  }

  private static void AddQueues(
    ImmutableHashSet<string>.Builder scopes,
    BlackMaple.MachineFramework.Job job,
    int process
  )
  {
    foreach (var path in job.Processes[process - 1].Paths)
    {
      AddQueue(scopes, path.InputQueue);
      AddQueue(scopes, path.OutputQueue);
    }
  }

  private static void AddQueue(ImmutableHashSet<string>.Builder scopes, string queue)
  {
    if (!string.IsNullOrEmpty(queue))
      scopes.Add(Queue(queue));
  }
}
