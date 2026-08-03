/* Copyright (c) 2026, John Lenz

All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

    * Redistributions of source code must retain the above copyright
      notice, this list of conditions and the following disclaimer.

    * Redistributions in binary form must reproduce the above copyright
      notice, this list of conditions and the following disclaimer in the
      documentation and/or other materials provided with the distribution.

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

#nullable disable

using System;
using System.Collections.Immutable;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.Data.Sqlite;

namespace BlackMaple.MachineFramework
{
  internal sealed partial class Repository
  {
    public BasketMisload RecordBasketMisload(
      Guid misloadId,
      int? basketId,
      ImmutableSortedSet<Guid> contentEpisodeIds,
      BasketPosition detectedAt,
      BasketEvidenceSource source,
      string reason,
      DateTime timeUTC,
      EventLogMetadata metadata = null
    )
    {
      var normalizedPosition = NormalizeBasketPosition(detectedAt);
      var normalizedSource = NormalizeBasketEvidenceSource(source);
      var normalizedMetadata = NormalizeEventLogMetadata(metadata);
      var normalizedReason = NormalizeOptional(reason);
      ValidateBasketMisload(
        misloadId,
        basketId,
        contentEpisodeIds,
        normalizedPosition,
        normalizedReason
      );
      var fingerprint = BasketMisloadFingerprint(
        basketId,
        contentEpisodeIds,
        normalizedPosition,
        normalizedSource,
        normalizedReason
      );

      BasketMisload misload;
      LogEntry log = null;
      lock (_cfg)
      {
        using var trans = _connection.BeginTransaction();
        using (var existing = _connection.CreateCommand())
        {
          existing.Transaction = trans;
          existing.CommandText =
            "SELECT Fingerprint, Counter FROM basket_misloads WHERE MisloadId = $id";
          existing.Parameters.Add("id", SqliteType.Text).Value = misloadId.ToString("D");
          using var reader = existing.ExecuteReader();
          if (reader.Read())
          {
            if (reader.GetString(0) != fingerprint)
              throw new ConflictRequestException(
                $"Basket misload {misloadId:D} was already used with different arguments."
              );
            misload = BasketMisloadForCounter(reader.GetInt64(1), trans);
            trans.Commit();
            return misload;
          }
        }

        foreach (var contentEpisodeId in contentEpisodeIds)
          EnsureOpenBasketContentEpisode(contentEpisodeId, trans);

        var newLog = new NewEventLogEntry
        {
          Material = [],
          Pallet = basketId ?? 0,
          LogType = LogType.BasketMisload,
          LocationName = normalizedPosition.LocationTitle ?? normalizedPosition.Location.ToString(),
          LocationNum = normalizedPosition.LocationNum,
          Program = normalizedSource.Kind.ToString(),
          StartOfCycle = false,
          EndTimeUTC = timeUTC,
          Result = "RequiresInspection",
          ElapsedTime = TimeSpan.Zero,
          ActiveOperationTime = TimeSpan.Zero,
          Metadata = normalizedMetadata,
        };
        newLog.ProgramDetails.Add("sourceKind", normalizedSource.Kind.ToString());
        newLog.ProgramDetails.Add("sourceName", normalizedSource.Name);
        newLog.ProgramDetails.Add("location", normalizedPosition.Location.ToString());
        if (normalizedPosition.Zone is { } detectedZone)
          newLog.ProgramDetails.Add("zone", detectedZone.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(normalizedPosition.LocationTitle))
          newLog.ProgramDetails.Add("locationTitle", normalizedPosition.LocationTitle);
        newLog.ProgramDetails.Add("reason", normalizedReason);
        newLog.ProgramDetails.Add(
          "contentEpisodeCount",
          contentEpisodeIds.Count.ToString(CultureInfo.InvariantCulture)
        );
        log = AddLogEntry(trans, newLog, normalizedMetadata);
        InsertBasketEvidenceSource(log.Counter, normalizedSource, trans);
        using (var insert = _connection.CreateCommand())
        {
          insert.Transaction = trans;
          insert.CommandText =
            "INSERT INTO basket_misloads(MisloadId, Fingerprint, Counter, ResolutionId) VALUES($id, $fingerprint, $counter, NULL)";
          insert.Parameters.Add("id", SqliteType.Text).Value = misloadId.ToString("D");
          insert.Parameters.Add("fingerprint", SqliteType.Text).Value = fingerprint;
          insert.Parameters.Add("counter", SqliteType.Integer).Value = log.Counter;
          insert.ExecuteNonQuery();

          insert.CommandText =
            "INSERT INTO basket_misload_details(Counter, BasketNum, DetectedLocation, DetectedLocationNum, DetectedZone, DetectedLocationTitle, Reason) VALUES($counter, $basket, $location, $locationNum, $zone, $title, $reason)";
          insert.Parameters.Clear();
          insert.Parameters.Add("counter", SqliteType.Integer).Value = log.Counter;
          insert.Parameters.Add("basket", SqliteType.Integer).Value = basketId is { } number
            ? number
            : DBNull.Value;
          insert.Parameters.Add("location", SqliteType.Integer).Value = (int)
            normalizedPosition.Location;
          insert.Parameters.Add("locationNum", SqliteType.Integer).Value =
            normalizedPosition.LocationNum;
          insert.Parameters.Add("zone", SqliteType.Integer).Value = normalizedPosition.Zone
            is { } zone
            ? zone
            : DBNull.Value;
          insert.Parameters.Add("title", SqliteType.Text).Value = normalizedPosition.LocationTitle
            is { } title
            ? title
            : DBNull.Value;
          insert.Parameters.Add("reason", SqliteType.Text).Value = normalizedReason;
          insert.ExecuteNonQuery();

          foreach (var contentEpisodeId in contentEpisodeIds)
          {
            insert.CommandText =
              "INSERT INTO basket_misload_episodes(Counter, ContentEpisodeId) VALUES($counter, $episode)";
            insert.Parameters.Clear();
            insert.Parameters.Add("counter", SqliteType.Integer).Value = log.Counter;
            insert.Parameters.Add("episode", SqliteType.Text).Value = contentEpisodeId.ToString(
              "D"
            );
            insert.ExecuteNonQuery();
          }
          insert.CommandText =
            "INSERT INTO active_basket_misloads(MisloadCounter) VALUES($counter)";
          insert.Parameters.Clear();
          insert.Parameters.Add("counter", SqliteType.Integer).Value = log.Counter;
          insert.ExecuteNonQuery();
        }

        misload = new BasketMisload
        {
          MisloadId = misloadId,
          BasketId = basketId,
          ContentEpisodeIds = contentEpisodeIds,
          DetectedAt = normalizedPosition,
          Source = normalizedSource,
          Reason = normalizedReason,
          CorrelationId = normalizedMetadata.CorrelationId,
          TimeUTC = timeUTC,
          EventCounter = log.Counter,
        };
        trans.Commit();
      }
      _cfg.OnNewLogEntry(log, normalizedMetadata.ForeignId, this);
      return misload;
    }

    public BasketMisloadResolution ResolveBasketMisload(
      Guid resolutionId,
      Guid misloadId,
      BasketMisloadResolutionKind kind,
      BasketEvidenceSource source,
      DateTime timeUTC,
      string note = null,
      EventLogMetadata metadata = null
    )
    {
      if (resolutionId == Guid.Empty)
        throw new ArgumentException("Resolution ID can not be empty.", nameof(resolutionId));
      if (misloadId == Guid.Empty)
        throw new ArgumentException("Misload ID can not be empty.", nameof(misloadId));
      if (!Enum.IsDefined(kind))
        throw new ArgumentOutOfRangeException(nameof(kind));
      var normalizedSource = NormalizeBasketEvidenceSource(source);
      var normalizedMetadata = NormalizeEventLogMetadata(metadata);
      var normalizedNote = NormalizeOptional(note);
      var fingerprint = BasketMisloadResolutionFingerprint(
        misloadId,
        kind,
        normalizedSource,
        normalizedNote
      );

      BasketMisloadResolution resolution;
      LogEntry log = null;
      lock (_cfg)
      {
        using var trans = _connection.BeginTransaction();
        using (var existing = _connection.CreateCommand())
        {
          existing.Transaction = trans;
          existing.CommandText =
            "SELECT Fingerprint, Counter FROM basket_misload_resolutions WHERE ResolutionId = $id";
          existing.Parameters.Add("id", SqliteType.Text).Value = resolutionId.ToString("D");
          using var reader = existing.ExecuteReader();
          if (reader.Read())
          {
            if (reader.GetString(0) != fingerprint)
              throw new ConflictRequestException(
                $"Basket misload resolution {resolutionId:D} was already used with different arguments."
              );
            resolution = BasketMisloadResolutionForCounter(reader.GetInt64(1), trans);
            trans.Commit();
            return resolution;
          }
        }

        var misload = BasketMisloadForId(misloadId, trans);
        if (misload is null)
          throw new ConflictRequestException($"Basket misload {misloadId:D} does not exist.");
        using (var active = _connection.CreateCommand())
        {
          active.Transaction = trans;
          active.CommandText = "SELECT ResolutionId FROM basket_misloads WHERE MisloadId = $id";
          active.Parameters.Add("id", SqliteType.Text).Value = misloadId.ToString("D");
          if (active.ExecuteScalar() is not null and not DBNull)
            throw new ConflictRequestException(
              $"Basket misload {misloadId:D} is already resolved."
            );
        }

        var newLog = new NewEventLogEntry
        {
          Material = [],
          Pallet = misload.BasketId ?? 0,
          LogType = LogType.BasketMisloadResolution,
          LocationName = misload.DetectedAt.LocationTitle ?? misload.DetectedAt.Location.ToString(),
          LocationNum = misload.DetectedAt.LocationNum,
          Program = normalizedSource.Kind.ToString(),
          StartOfCycle = false,
          EndTimeUTC = timeUTC,
          Result = kind.ToString(),
          ElapsedTime = TimeSpan.Zero,
          ActiveOperationTime = TimeSpan.Zero,
          Metadata = normalizedMetadata,
        };
        newLog.ProgramDetails.Add("sourceKind", normalizedSource.Kind.ToString());
        newLog.ProgramDetails.Add("sourceName", normalizedSource.Name);
        newLog.ProgramDetails.Add("location", misload.DetectedAt.Location.ToString());
        if (misload.DetectedAt.Zone is { } detectedZone)
          newLog.ProgramDetails.Add("zone", detectedZone.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(misload.DetectedAt.LocationTitle))
          newLog.ProgramDetails.Add("locationTitle", misload.DetectedAt.LocationTitle);
        if (normalizedNote is not null)
          newLog.ProgramDetails.Add("note", normalizedNote);
        log = AddLogEntry(trans, newLog, normalizedMetadata);
        InsertBasketEvidenceSource(log.Counter, normalizedSource, trans);
        using (var insert = _connection.CreateCommand())
        {
          insert.Transaction = trans;
          insert.CommandText =
            "INSERT INTO basket_misload_resolutions(ResolutionId, Fingerprint, MisloadId, Counter, ResolutionKind, Note) VALUES($id, $fingerprint, $misload, $counter, $kind, $note)";
          insert.Parameters.Add("id", SqliteType.Text).Value = resolutionId.ToString("D");
          insert.Parameters.Add("fingerprint", SqliteType.Text).Value = fingerprint;
          insert.Parameters.Add("misload", SqliteType.Text).Value = misloadId.ToString("D");
          insert.Parameters.Add("counter", SqliteType.Integer).Value = log.Counter;
          insert.Parameters.Add("kind", SqliteType.Integer).Value = (int)kind;
          insert.Parameters.Add("note", SqliteType.Text).Value = normalizedNote is { } value
            ? value
            : DBNull.Value;
          insert.ExecuteNonQuery();
          insert.CommandText =
            "UPDATE basket_misloads SET ResolutionId = $resolution WHERE MisloadId = $misload AND ResolutionId IS NULL";
          insert.Parameters.Clear();
          insert.Parameters.Add("resolution", SqliteType.Text).Value = resolutionId.ToString("D");
          insert.Parameters.Add("misload", SqliteType.Text).Value = misloadId.ToString("D");
          if (insert.ExecuteNonQuery() != 1)
            throw new ConflictRequestException(
              $"Basket misload {misloadId:D} is already resolved."
            );
          insert.CommandText = "DELETE FROM active_basket_misloads WHERE MisloadCounter = $counter";
          insert.Parameters.Clear();
          insert.Parameters.Add("counter", SqliteType.Integer).Value = misload.EventCounter;
          insert.ExecuteNonQuery();
        }

        resolution = new BasketMisloadResolution
        {
          ResolutionId = resolutionId,
          MisloadId = misloadId,
          Kind = kind,
          Source = normalizedSource,
          Note = normalizedNote,
          CorrelationId = normalizedMetadata.CorrelationId,
          TimeUTC = timeUTC,
          EventCounter = log.Counter,
        };
        trans.Commit();
      }
      _cfg.OnNewLogEntry(log, normalizedMetadata.ForeignId, this);
      return resolution;
    }

    public ImmutableList<BasketMisload> GetActiveBasketMisloads()
    {
      using var trans = _connection.BeginTransaction();
      using var cmd = _connection.CreateCommand();
      cmd.Transaction = trans;
      cmd.CommandText = "SELECT MisloadCounter FROM active_basket_misloads ORDER BY MisloadCounter";
      using var reader = cmd.ExecuteReader();
      var counters = ImmutableList.CreateBuilder<long>();
      while (reader.Read())
        counters.Add(reader.GetInt64(0));
      var misloads = counters
        .Select(counter => BasketMisloadForCounter(counter, trans))
        .ToImmutableList();
      trans.Commit();
      return misloads;
    }

    [return: MaybeNull]
    public BasketMisload GetBasketMisload(Guid misloadId)
    {
      if (misloadId == Guid.Empty)
        throw new ArgumentException("Misload ID can not be empty.", nameof(misloadId));
      using var trans = _connection.BeginTransaction();
      var misload = BasketMisloadForId(misloadId, trans);
      trans.Commit();
      return misload;
    }

    public ImmutableList<BasketMisloadResolution> GetBasketMisloadResolutions(
      Guid? misloadId = null
    )
    {
      using var trans = _connection.BeginTransaction();
      using var cmd = _connection.CreateCommand();
      cmd.Transaction = trans;
      cmd.CommandText =
        "SELECT Counter FROM basket_misload_resolutions "
        + (misloadId.HasValue ? "WHERE MisloadId = $misload " : "")
        + "ORDER BY Counter";
      if (misloadId.HasValue)
        cmd.Parameters.Add("misload", SqliteType.Text).Value = misloadId.Value.ToString("D");
      using var reader = cmd.ExecuteReader();
      var counters = ImmutableList.CreateBuilder<long>();
      while (reader.Read())
        counters.Add(reader.GetInt64(0));
      var resolutions = counters
        .Select(counter => BasketMisloadResolutionForCounter(counter, trans))
        .ToImmutableList();
      trans.Commit();
      return resolutions;
    }

    private BasketMisload BasketMisloadForId(Guid misloadId, IDbTransaction trans)
    {
      using var cmd = _connection.CreateCommand();
      ((IDbCommand)cmd).Transaction = trans;
      cmd.CommandText = "SELECT Counter FROM basket_misloads WHERE MisloadId = $id";
      cmd.Parameters.Add("id", SqliteType.Text).Value = misloadId.ToString("D");
      return cmd.ExecuteScalar() is long counter ? BasketMisloadForCounter(counter, trans) : null;
    }

    private BasketMisload BasketMisloadForCounter(long counter, IDbTransaction trans)
    {
      using var cmd = _connection.CreateCommand();
      ((IDbCommand)cmd).Transaction = trans;
      cmd.CommandText =
        "SELECT m.MisloadId, d.BasketNum, d.DetectedLocation, d.DetectedLocationNum, d.DetectedZone, d.DetectedLocationTitle, d.Reason, s.TimeUTC "
        + "FROM basket_misloads m JOIN basket_misload_details d ON d.Counter = m.Counter "
        + "JOIN stations s ON s.Counter = m.Counter WHERE m.Counter = $counter";
      cmd.Parameters.Add("counter", SqliteType.Integer).Value = counter;
      using var reader = cmd.ExecuteReader();
      if (!reader.Read())
        return null;
      var misloadId = Guid.Parse(reader.GetString(0));
      var basketId = reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1);
      var detectedAt = new BasketPosition
      {
        Location = (BasketLocationEnum)reader.GetInt32(2),
        LocationNum = reader.GetInt32(3),
        Zone = reader.IsDBNull(4) ? null : reader.GetInt32(4),
        LocationTitle = reader.IsDBNull(5) ? null : reader.GetString(5),
      };
      var reason = reader.GetString(6);
      var timeUTC = new DateTime(reader.GetInt64(7), DateTimeKind.Utc);
      reader.Close();
      cmd.CommandText =
        "SELECT ContentEpisodeId FROM basket_misload_episodes WHERE Counter = $counter ORDER BY ContentEpisodeId";
      using var episodeReader = cmd.ExecuteReader();
      var episodes = ImmutableSortedSet.CreateBuilder<Guid>();
      while (episodeReader.Read())
        episodes.Add(Guid.Parse(episodeReader.GetString(0)));
      episodeReader.Close();
      var (source, correlationId) = BasketEvidenceSourceForCounter(counter, trans);
      return new BasketMisload
      {
        MisloadId = misloadId,
        BasketId = basketId,
        ContentEpisodeIds = episodes.ToImmutable(),
        DetectedAt = detectedAt,
        Source = source,
        Reason = reason,
        CorrelationId = correlationId,
        TimeUTC = timeUTC,
        EventCounter = counter,
      };
    }

    private BasketMisloadResolution BasketMisloadResolutionForCounter(
      long counter,
      IDbTransaction trans
    )
    {
      using var cmd = _connection.CreateCommand();
      ((IDbCommand)cmd).Transaction = trans;
      cmd.CommandText =
        "SELECT r.ResolutionId, r.MisloadId, r.ResolutionKind, r.Note, s.TimeUTC "
        + "FROM basket_misload_resolutions r JOIN stations s ON s.Counter = r.Counter WHERE r.Counter = $counter";
      cmd.Parameters.Add("counter", SqliteType.Integer).Value = counter;
      using var reader = cmd.ExecuteReader();
      if (!reader.Read())
        return null;
      var resolutionId = Guid.Parse(reader.GetString(0));
      var misloadId = Guid.Parse(reader.GetString(1));
      var kind = (BasketMisloadResolutionKind)reader.GetInt32(2);
      var note = reader.IsDBNull(3) ? null : reader.GetString(3);
      var timeUTC = new DateTime(reader.GetInt64(4), DateTimeKind.Utc);
      reader.Close();
      var (source, correlationId) = BasketEvidenceSourceForCounter(counter, trans);
      return new BasketMisloadResolution
      {
        ResolutionId = resolutionId,
        MisloadId = misloadId,
        Kind = kind,
        Source = source,
        Note = note,
        CorrelationId = correlationId,
        TimeUTC = timeUTC,
        EventCounter = counter,
      };
    }

    private static void ValidateBasketMisload(
      Guid misloadId,
      int? basketId,
      ImmutableSortedSet<Guid> contentEpisodeIds,
      BasketPosition detectedAt,
      string reason
    )
    {
      if (misloadId == Guid.Empty)
        throw new ArgumentException("Misload ID can not be empty.", nameof(misloadId));
      if (basketId is <= 0)
        throw new ArgumentOutOfRangeException(nameof(basketId));
      ArgumentNullException.ThrowIfNull(contentEpisodeIds);
      if (contentEpisodeIds.Contains(Guid.Empty))
        throw new ArgumentException(
          "Content episode IDs can not contain an empty UUID.",
          nameof(contentEpisodeIds)
        );
      if (basketId is null && contentEpisodeIds.IsEmpty)
        throw new ArgumentException(
          "A basket misload requires a basket number, content episodes, or both."
        );
      ArgumentNullException.ThrowIfNull(detectedAt);
      if (detectedAt.LocationNum <= 0 || detectedAt.Zone is <= 0)
        throw new ArgumentException(
          "Detected position numbers must be positive.",
          nameof(detectedAt)
        );
      if (reason is null)
        throw new ArgumentException("A basket misload reason is required.", nameof(reason));
    }

    private static string BasketMisloadFingerprint(
      int? basketId,
      ImmutableSortedSet<Guid> contentEpisodeIds,
      BasketPosition detectedAt,
      BasketEvidenceSource source,
      string reason
    )
    {
      var fingerprint = new StringBuilder();
      AppendFingerprint(fingerprint, basketId?.ToString(CultureInfo.InvariantCulture));
      foreach (var episodeId in contentEpisodeIds)
        AppendFingerprint(fingerprint, episodeId.ToString("D"));
      AppendBasketPositionFingerprint(fingerprint, detectedAt);
      AppendBasketEvidenceSourceFingerprint(fingerprint, source);
      AppendFingerprint(fingerprint, reason);
      return fingerprint.ToString();
    }

    private static string BasketMisloadResolutionFingerprint(
      Guid misloadId,
      BasketMisloadResolutionKind kind,
      BasketEvidenceSource source,
      string note
    )
    {
      var fingerprint = new StringBuilder();
      AppendFingerprint(fingerprint, misloadId.ToString("D"));
      AppendFingerprint(fingerprint, kind.ToString());
      AppendBasketEvidenceSourceFingerprint(fingerprint, source);
      AppendFingerprint(fingerprint, note);
      return fingerprint.ToString();
    }
  }
}
