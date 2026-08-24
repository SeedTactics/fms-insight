/* Copyright (c) 2026, John Lenz

All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

    * Redistributions of source code must retain the above copyright
      notice, this list of conditions and the following disclaimer.

    * Redistributions in binary form must reproduce the above
      copyright notice, this list of conditions and the following disclaimer
      in the documentation and/or other materials provided with the distribution.

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
using System.Collections.Generic;
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
    private sealed record AddedBasketObservation(
      BasketObservation Observation,
      LogEntry Log,
      bool Created
    );

    public BasketObservation RecordBasketObservation(
      Guid observationId,
      int basketId,
      BasketPosition position,
      ImmutableSortedSet<Guid> contentEpisodeIds,
      BasketEvidenceSource source,
      DateTime timeUTC,
      EventLogMetadata metadata = null,
      string note = null
    )
    {
      var normalizedSource = NormalizeBasketEvidenceSource(source);
      var normalizedPosition = NormalizeBasketPosition(position);
      var normalizedMetadata = NormalizeEventLogMetadata(metadata);
      var normalizedNote = NormalizeOptional(note);
      ValidateBasketObservation(observationId, basketId, normalizedPosition, contentEpisodeIds);
      var fingerprint = BasketObservationFingerprint(
        basketId,
        normalizedPosition,
        contentEpisodeIds,
        normalizedSource,
        normalizedNote
      );

      AddedBasketObservation added;
      lock (_cfg)
      {
        using var trans = _connection.BeginTransaction();
        added = AddBasketObservation(
          observationId,
          basketId,
          normalizedPosition,
          contentEpisodeIds,
          normalizedSource,
          timeUTC,
          normalizedMetadata,
          trans,
          fingerprint,
          normalizedNote
        );
        trans.Commit();
      }
      if (added.Created)
        _cfg.OnNewLogEntry(added.Log, normalizedMetadata.ForeignId, this);
      return added.Observation;
    }

    public BasketObservationCorrectionResult CorrectBasketObservation(
      Guid correctionId,
      Guid targetObservationId,
      [AllowNull] BasketObservationReplacement replacement,
      BasketEvidenceSource source,
      DateTime timeUTC,
      string note = null,
      EventLogMetadata metadata = null
    )
    {
      if (correctionId == Guid.Empty)
        throw new ArgumentException("Correction ID can not be empty.", nameof(correctionId));
      if (targetObservationId == Guid.Empty)
        throw new ArgumentException(
          "Target observation ID can not be empty.",
          nameof(targetObservationId)
        );
      if (replacement?.ObservationId == targetObservationId)
        throw new ArgumentException(
          "The replacement observation must have a new observation ID.",
          nameof(replacement)
        );

      var normalizedSource = NormalizeBasketEvidenceSource(source);
      var normalizedMetadata = NormalizeEventLogMetadata(metadata);
      var normalizedNote = NormalizeOptional(note);
      var normalizedReplacement = replacement is null
        ? null
        : replacement with
        {
          Position = NormalizeBasketPosition(replacement.Position),
          Source = NormalizeBasketEvidenceSource(replacement.Source),
        };
      if (normalizedReplacement is not null)
        ValidateBasketObservation(
          normalizedReplacement.ObservationId,
          normalizedReplacement.BasketId,
          normalizedReplacement.Position,
          normalizedReplacement.ContentEpisodeIds
        );
      var fingerprint = BasketObservationCorrectionFingerprint(
        targetObservationId,
        normalizedReplacement,
        normalizedSource,
        normalizedNote
      );

      BasketObservationCorrectionResult result;
      var newLogs = ImmutableList.CreateBuilder<LogEntry>();
      lock (_cfg)
      {
        using var trans = _connection.BeginTransaction();
        using (var existing = _connection.CreateCommand())
        {
          existing.Transaction = trans;
          existing.CommandText =
            "SELECT Fingerprint FROM basket_observation_corrections WHERE CorrectionId = $id";
          existing.Parameters.Add("id", SqliteType.Text).Value = correctionId.ToString("D");
          var existingFingerprint = existing.ExecuteScalar() as string;
          if (existingFingerprint is not null)
          {
            if (existingFingerprint != fingerprint)
              throw new ConflictRequestException(
                $"Basket observation correction {correctionId:D} was already used with different arguments."
              );
            result = BasketObservationCorrectionResultForId(correctionId, trans);
            trans.Commit();
            return result;
          }
        }

        var target = BasketObservationForId(targetObservationId, trans);
        if (target is null)
          throw new ConflictRequestException(
            $"Basket observation {targetObservationId:D} does not exist."
          );
        using (var current = _connection.CreateCommand())
        {
          current.Transaction = trans;
          current.CommandText =
            "SELECT SupersededByCorrectionId FROM basket_observations WHERE ObservationId = $id";
          current.Parameters.Add("id", SqliteType.Text).Value = targetObservationId.ToString("D");
          if (current.ExecuteScalar() is not null and not DBNull)
            throw new ConflictRequestException(
              $"Basket observation {targetObservationId:D} is already corrected."
            );
        }

        if (normalizedReplacement is not null)
        {
          using var replacementExists = _connection.CreateCommand();
          replacementExists.Transaction = trans;
          replacementExists.CommandText =
            "SELECT 1 FROM basket_observations WHERE ObservationId = $id";
          replacementExists.Parameters.Add("id", SqliteType.Text).Value =
            normalizedReplacement.ObservationId.ToString("D");
          if (replacementExists.ExecuteScalar() is not null)
            throw new ConflictRequestException(
              $"Basket observation {normalizedReplacement.ObservationId:D} already exists."
            );
        }

        var correctionEntry = new NewEventLogEntry
        {
          Material = [],
          Pallet = target.BasketId,
          LogType = LogType.BasketObservationCorrection,
          LocationName = target.Position.LocationTitle ?? "Basket Observation",
          LocationNum = target.Position.LocationNum,
          Program = "Observation Correction",
          StartOfCycle = false,
          EndTimeUTC = timeUTC,
          Result = normalizedReplacement is null ? "Retracted" : "Replaced",
          ElapsedTime = TimeSpan.Zero,
          ActiveOperationTime = TimeSpan.Zero,
          Metadata = normalizedMetadata,
        };
        correctionEntry.ProgramDetails.Add("sourceKind", normalizedSource.Kind.ToString());
        correctionEntry.ProgramDetails.Add("sourceName", normalizedSource.Name);
        if (normalizedNote is not null)
          correctionEntry.ProgramDetails.Add("note", normalizedNote);
        var correctionLog = AddLogEntry(trans, correctionEntry, normalizedMetadata);
        InsertBasketEvidenceSource(correctionLog.Counter, normalizedSource, trans);
        using (var insertCorrection = _connection.CreateCommand())
        {
          insertCorrection.Transaction = trans;
          insertCorrection.CommandText =
            "INSERT INTO basket_observation_corrections(CorrectionId, Fingerprint, TargetObservationId, ReplacementObservationId, Counter, Note) VALUES($id, $fingerprint, $target, $replacement, $counter, $note)";
          insertCorrection.Parameters.Add("id", SqliteType.Text).Value = correctionId.ToString("D");
          insertCorrection.Parameters.Add("fingerprint", SqliteType.Text).Value = fingerprint;
          insertCorrection.Parameters.Add("target", SqliteType.Text).Value =
            targetObservationId.ToString("D");
          insertCorrection.Parameters.Add("replacement", SqliteType.Text).Value =
            normalizedReplacement is null
              ? DBNull.Value
              : normalizedReplacement.ObservationId.ToString("D");
          insertCorrection.Parameters.Add("counter", SqliteType.Integer).Value =
            correctionLog.Counter;
          insertCorrection.Parameters.Add("note", SqliteType.Text).Value = normalizedNote is null
            ? DBNull.Value
            : normalizedNote;
          insertCorrection.ExecuteNonQuery();
        }
        using (var supersede = _connection.CreateCommand())
        {
          supersede.Transaction = trans;
          supersede.CommandText =
            "UPDATE basket_observations SET SupersededByCorrectionId = $correction WHERE ObservationId = $target";
          supersede.Parameters.Add("correction", SqliteType.Text).Value = correctionId.ToString(
            "D"
          );
          supersede.Parameters.Add("target", SqliteType.Text).Value = targetObservationId.ToString(
            "D"
          );
          supersede.ExecuteNonQuery();
        }

        RebuildActiveBasketObservationEpisodes(target.ContentEpisodeIds, trans);

        BasketObservation replacementObservation = null;
        newLogs.Add(correctionLog);
        if (normalizedReplacement is not null)
        {
          var added = AddBasketObservation(
            normalizedReplacement.ObservationId,
            normalizedReplacement.BasketId,
            normalizedReplacement.Position,
            normalizedReplacement.ContentEpisodeIds,
            normalizedReplacement.Source,
            timeUTC,
            normalizedMetadata,
            trans
          );
          replacementObservation = added.Observation;
          newLogs.Add(added.Log);
        }

        result = new BasketObservationCorrectionResult
        {
          Correction = new BasketObservationCorrection
          {
            CorrectionId = correctionId,
            TargetObservationId = targetObservationId,
            ReplacementObservationId = normalizedReplacement?.ObservationId,
            Source = normalizedSource,
            Note = normalizedNote,
            CorrelationId = normalizedMetadata.CorrelationId,
            TimeUTC = timeUTC,
            EventCounter = correctionLog.Counter,
          },
          Replacement = replacementObservation,
        };
        trans.Commit();
      }

      foreach (var log in newLogs)
        _cfg.OnNewLogEntry(log, normalizedMetadata.ForeignId, this);
      return result;
    }

    public ImmutableList<ActiveBasketObservationEvidence> GetActiveBasketObservationEvidence(
      int? basketNum = null
    )
    {
      using var trans = _connection.BeginTransaction();
      using var cmd = _connection.CreateCommand();
      cmd.Transaction = trans;
      cmd.CommandText =
        "WITH active AS ("
        + "SELECT o.Counter, s.Pallet, ROW_NUMBER() OVER (PARTITION BY s.Pallet ORDER BY o.Counter DESC) AS PositionRank "
        + "FROM basket_observations o JOIN stations s ON s.Counter = o.Counter "
        + "WHERE o.SupersededByCorrectionId IS NULL AND s.StationLoc = $type "
        + (basketNum.HasValue ? "AND s.Pallet = $num " : "")
        + ") SELECT Counter, PositionRank = 1 AS IsCurrentPositionEvidence FROM active WHERE PositionRank = 1 "
        + "OR EXISTS(SELECT 1 FROM current_basket_observation_episodes c WHERE c.ObservationCounter = active.Counter) "
        + "ORDER BY Pallet, Counter";
      cmd.Parameters.Add("type", SqliteType.Integer).Value = (int)LogType.BasketObservation;
      if (basketNum.HasValue)
        cmd.Parameters.Add("num", SqliteType.Integer).Value = basketNum.Value;
      using var reader = cmd.ExecuteReader();
      var evidence = ImmutableList.CreateBuilder<ActiveBasketObservationEvidence>();
      while (reader.Read())
      {
        evidence.Add(
          ActiveBasketObservationEvidenceForCounter(reader.GetInt64(0), reader.GetBoolean(1), trans)
        );
      }
      trans.Commit();
      return evidence.ToImmutable();
    }

    [return: MaybeNull]
    public BasketObservation GetBasketObservation(Guid observationId)
    {
      if (observationId == Guid.Empty)
        throw new ArgumentException("Observation ID can not be empty.", nameof(observationId));
      using var trans = _connection.BeginTransaction();
      var observation = BasketObservationForId(observationId, trans);
      trans.Commit();
      return observation;
    }

    public ImmutableList<BasketObservationCorrection> GetBasketObservationCorrections(
      Guid? targetObservationId = null
    )
    {
      using var trans = _connection.BeginTransaction();
      using var cmd = _connection.CreateCommand();
      cmd.Transaction = trans;
      cmd.CommandText =
        "SELECT Counter FROM basket_observation_corrections "
        + (targetObservationId.HasValue ? "WHERE TargetObservationId = $target " : "")
        + "ORDER BY Counter";
      if (targetObservationId.HasValue)
        cmd.Parameters.Add("target", SqliteType.Text).Value = targetObservationId.Value.ToString(
          "D"
        );
      using var reader = cmd.ExecuteReader();
      var counters = ImmutableList.CreateBuilder<long>();
      while (reader.Read())
        counters.Add(reader.GetInt64(0));
      var corrections = counters
        .Select(counter => BasketObservationCorrectionForCounter(counter, trans))
        .ToImmutableList();
      trans.Commit();
      return corrections;
    }

    private AddedBasketObservation AddBasketObservation(
      Guid observationId,
      int basketId,
      BasketPosition position,
      ImmutableSortedSet<Guid> contentEpisodeIds,
      BasketEvidenceSource source,
      DateTime timeUTC,
      EventLogMetadata metadata,
      IDbTransaction trans,
      string fingerprint = null,
      string note = null
    )
    {
      fingerprint ??= BasketObservationFingerprint(
        basketId,
        position,
        contentEpisodeIds,
        source,
        note
      );
      using (var existing = _connection.CreateCommand())
      {
        ((IDbCommand)existing).Transaction = trans;
        existing.CommandText =
          "SELECT Fingerprint, Counter FROM basket_observations WHERE ObservationId = $id";
        existing.Parameters.Add("id", SqliteType.Text).Value = observationId.ToString("D");
        using var reader = existing.ExecuteReader();
        if (reader.Read())
        {
          if (reader.GetString(0) != fingerprint)
            throw new ConflictRequestException(
              $"Basket observation {observationId:D} was already used with different arguments."
            );
          var counter = reader.GetInt64(1);
          return new AddedBasketObservation(
            BasketObservationForCounter(counter, trans),
            LogForCounter(counter, trans),
            Created: false
          );
        }
      }

      foreach (var contentEpisodeId in contentEpisodeIds)
      {
        EnsureOpenBasketContentEpisode(contentEpisodeId, trans);
        using var current = _connection.CreateCommand();
        ((IDbCommand)current).Transaction = trans;
        current.CommandText =
          "SELECT BasketNum FROM current_basket_observation_episodes WHERE ContentEpisodeId = $id";
        current.Parameters.Add("id", SqliteType.Text).Value = contentEpisodeId.ToString("D");
        if (
          current.ExecuteScalar() is { } existingBasket
          && Convert.ToInt32(existingBasket, CultureInfo.InvariantCulture) != basketId
        )
          throw new ConflictRequestException(
            $"Basket content episode {contentEpisodeId:D} is already associated with basket {Convert.ToInt32(existingBasket, CultureInfo.InvariantCulture)}."
          );
      }

      var newLog = new NewEventLogEntry
      {
        Material = [],
        Pallet = basketId,
        LogType = LogType.BasketObservation,
        LocationName = position.LocationTitle ?? "Basket Observation",
        LocationNum = position.LocationNum,
        Program = "Observation",
        StartOfCycle = false,
        EndTimeUTC = timeUTC,
        Result = "Observed",
        ElapsedTime = TimeSpan.Zero,
        ActiveOperationTime = TimeSpan.Zero,
        Metadata = metadata,
      };
      newLog.ProgramDetails.Add("observationId", observationId.ToString("D"));
      newLog.ProgramDetails.Add("sourceKind", source.Kind.ToString());
      newLog.ProgramDetails.Add("sourceName", source.Name);
      newLog.ProgramDetails.Add("location", position.Location.ToString());
      if (position.Zone is { } zone)
        newLog.ProgramDetails.Add("zone", zone.ToString(CultureInfo.InvariantCulture));
      if (!string.IsNullOrWhiteSpace(position.LocationTitle))
        newLog.ProgramDetails.Add("locationTitle", position.LocationTitle);
      newLog.ProgramDetails.Add(
        "episodeCount",
        contentEpisodeIds.Count.ToString(CultureInfo.InvariantCulture)
      );
      if (note is not null)
        newLog.ProgramDetails.Add("note", note);
      var log = AddLogEntry(trans, newLog, metadata);
      InsertBasketEvidenceSource(log.Counter, source, trans);
      using (var insert = _connection.CreateCommand())
      {
        ((IDbCommand)insert).Transaction = trans;
        insert.CommandText =
          "INSERT INTO basket_observations(ObservationId, Fingerprint, Counter, SupersededByCorrectionId) VALUES($id, $fingerprint, $counter, NULL)";
        insert.Parameters.Add("id", SqliteType.Text).Value = observationId.ToString("D");
        insert.Parameters.Add("fingerprint", SqliteType.Text).Value = fingerprint;
        insert.Parameters.Add("counter", SqliteType.Integer).Value = log.Counter;
        insert.ExecuteNonQuery();

        insert.CommandText =
          "INSERT INTO basket_observation_details(Counter, PositionLocation, PositionLocationNum, PositionZone, PositionLocationTitle, Note) VALUES($counter, $location, $locationNum, $zone, $title, $note)";
        insert.Parameters.Clear();
        insert.Parameters.Add("counter", SqliteType.Integer).Value = log.Counter;
        insert.Parameters.Add("location", SqliteType.Integer).Value = (int)position.Location;
        insert.Parameters.Add("locationNum", SqliteType.Integer).Value = position.LocationNum;
        insert.Parameters.Add("zone", SqliteType.Integer).Value = position.Zone is { } positionZone
          ? positionZone
          : DBNull.Value;
        insert.Parameters.Add("title", SqliteType.Text).Value = position.LocationTitle is { } title
          ? title
          : DBNull.Value;
        insert.Parameters.Add("note", SqliteType.Text).Value = note is { } value
          ? value
          : DBNull.Value;
        insert.ExecuteNonQuery();

        foreach (var contentEpisodeId in contentEpisodeIds)
        {
          insert.CommandText =
            "INSERT INTO basket_observation_episodes(Counter, ContentEpisodeId) VALUES($counter, $episode)";
          insert.Parameters.Clear();
          insert.Parameters.Add("counter", SqliteType.Integer).Value = log.Counter;
          insert.Parameters.Add("episode", SqliteType.Text).Value = contentEpisodeId.ToString("D");
          insert.ExecuteNonQuery();
          insert.CommandText =
            "INSERT INTO current_basket_observation_episodes(ContentEpisodeId, ObservationCounter, BasketNum) VALUES($episode, $counter, $basket) "
            + "ON CONFLICT(ContentEpisodeId) DO UPDATE SET ObservationCounter = excluded.ObservationCounter, BasketNum = excluded.BasketNum";
          insert.Parameters.Clear();
          insert.Parameters.Add("episode", SqliteType.Text).Value = contentEpisodeId.ToString("D");
          insert.Parameters.Add("counter", SqliteType.Integer).Value = log.Counter;
          insert.Parameters.Add("basket", SqliteType.Integer).Value = basketId;
          insert.ExecuteNonQuery();
        }
      }

      return new AddedBasketObservation(
        new BasketObservation
        {
          ObservationId = observationId,
          BasketId = basketId,
          Position = position,
          ContentEpisodeIds = contentEpisodeIds,
          Source = source,
          Note = note,
          CorrelationId = metadata.CorrelationId,
          TimeUTC = timeUTC,
          EventCounter = log.Counter,
        },
        log,
        Created: true
      );
    }

    private void RebuildActiveBasketObservationEpisodes(
      IEnumerable<Guid> contentEpisodeIds,
      IDbTransaction trans
    )
    {
      using var cmd = _connection.CreateCommand();
      ((IDbCommand)cmd).Transaction = trans;
      cmd.Parameters.Add("id", SqliteType.Text);

      foreach (var contentEpisodeId in contentEpisodeIds)
      {
        cmd.CommandText =
          "DELETE FROM current_basket_observation_episodes WHERE ContentEpisodeId = $id";
        cmd.Parameters["id"].Value = contentEpisodeId.ToString("D");
        cmd.ExecuteNonQuery();

        cmd.CommandText =
          "INSERT INTO current_basket_observation_episodes(ContentEpisodeId, ObservationCounter, BasketNum) "
          + "SELECT e.ContentEpisodeId, o.Counter, s.Pallet "
          + "FROM basket_observation_episodes e "
          + "JOIN basket_observations o ON o.Counter = e.Counter "
          + "JOIN stations s ON s.Counter = o.Counter "
          + "WHERE e.ContentEpisodeId = $id AND o.SupersededByCorrectionId IS NULL "
          + "AND NOT EXISTS(SELECT 1 FROM basket_cycle_content_episode_ids f WHERE f.BasketContentEpisodeId = e.ContentEpisodeId) "
          + "ORDER BY o.Counter DESC LIMIT 1";
        cmd.ExecuteNonQuery();
      }
    }

    private BasketObservation BasketObservationForId(Guid observationId, IDbTransaction trans)
    {
      using var cmd = _connection.CreateCommand();
      ((IDbCommand)cmd).Transaction = trans;
      cmd.CommandText = "SELECT Counter FROM basket_observations WHERE ObservationId = $id";
      cmd.Parameters.Add("id", SqliteType.Text).Value = observationId.ToString("D");
      return cmd.ExecuteScalar() is long counter
        ? BasketObservationForCounter(counter, trans)
        : null;
    }

    private BasketObservation BasketObservationForCounter(long counter, IDbTransaction trans)
    {
      using var cmd = _connection.CreateCommand();
      ((IDbCommand)cmd).Transaction = trans;
      cmd.CommandText =
        "SELECT a.ObservationId, s.Pallet, d.PositionLocation, d.PositionLocationNum, d.PositionZone, d.PositionLocationTitle, d.Note, s.TimeUTC "
        + "FROM basket_observations a JOIN stations s ON s.Counter = a.Counter "
        + "JOIN basket_observation_details d ON d.Counter = a.Counter WHERE a.Counter = $counter";
      cmd.Parameters.Add("counter", SqliteType.Integer).Value = counter;
      using var reader = cmd.ExecuteReader();
      if (!reader.Read())
        return null;
      var observationId = Guid.Parse(reader.GetString(0));
      var basketId = reader.GetInt32(1);
      var position = new BasketPosition
      {
        Location = (BasketLocationEnum)reader.GetInt32(2),
        LocationNum = reader.GetInt32(3),
        Zone = reader.IsDBNull(4) ? null : reader.GetInt32(4),
        LocationTitle = reader.IsDBNull(5) ? null : reader.GetString(5),
      };
      var note = reader.IsDBNull(6) ? null : reader.GetString(6);
      var timeUTC = new DateTime(reader.GetInt64(7), DateTimeKind.Utc);
      reader.Close();

      cmd.CommandText =
        "SELECT ContentEpisodeId FROM basket_observation_episodes WHERE Counter = $counter ORDER BY ContentEpisodeId";
      using var episodeReader = cmd.ExecuteReader();
      var episodes = ImmutableSortedSet.CreateBuilder<Guid>();
      while (episodeReader.Read())
        episodes.Add(Guid.Parse(episodeReader.GetString(0)));
      episodeReader.Close();
      var (source, correlationId) = BasketEvidenceSourceForCounter(counter, trans);
      return new BasketObservation
      {
        ObservationId = observationId,
        BasketId = basketId,
        Position = position,
        ContentEpisodeIds = episodes.ToImmutable(),
        Source = source,
        Note = note,
        CorrelationId = correlationId,
        TimeUTC = timeUTC,
        EventCounter = counter,
      };
    }

    private ActiveBasketObservationEvidence ActiveBasketObservationEvidenceForCounter(
      long counter,
      bool isCurrentPositionEvidence,
      IDbTransaction trans
    )
    {
      var observation = BasketObservationForCounter(counter, trans);
      using var cmd = _connection.CreateCommand();
      ((IDbCommand)cmd).Transaction = trans;
      cmd.CommandText =
        "SELECT ContentEpisodeId FROM current_basket_observation_episodes WHERE ObservationCounter = $counter ORDER BY ContentEpisodeId";
      cmd.Parameters.Add("counter", SqliteType.Integer).Value = counter;
      using var reader = cmd.ExecuteReader();
      var activeEpisodes = ImmutableSortedSet.CreateBuilder<Guid>();
      while (reader.Read())
        activeEpisodes.Add(Guid.Parse(reader.GetString(0)));
      var activeContentEpisodeIds = activeEpisodes.ToImmutable();
      if (!isCurrentPositionEvidence && activeContentEpisodeIds.IsEmpty)
        throw new InvalidOperationException(
          $"Basket observation {observation.ObservationId:D} has no active evidence."
        );
      return new ActiveBasketObservationEvidence
      {
        Observation = observation,
        IsCurrentPositionEvidence = isCurrentPositionEvidence,
        ActiveContentEpisodeIds = activeContentEpisodeIds,
      };
    }

    private BasketObservationCorrectionResult BasketObservationCorrectionResultForId(
      Guid correctionId,
      IDbTransaction trans
    )
    {
      using var cmd = _connection.CreateCommand();
      ((IDbCommand)cmd).Transaction = trans;
      cmd.CommandText =
        "SELECT Counter, ReplacementObservationId FROM basket_observation_corrections WHERE CorrectionId = $id";
      cmd.Parameters.Add("id", SqliteType.Text).Value = correctionId.ToString("D");
      using var reader = cmd.ExecuteReader();
      if (!reader.Read())
        return null;
      var counter = reader.GetInt64(0);
      var replacementId = reader.IsDBNull(1) ? (Guid?)null : Guid.Parse(reader.GetString(1));
      reader.Close();
      return new BasketObservationCorrectionResult
      {
        Correction = BasketObservationCorrectionForCounter(counter, trans),
        Replacement = replacementId is { } id ? BasketObservationForId(id, trans) : null,
      };
    }

    private BasketObservationCorrection BasketObservationCorrectionForCounter(
      long counter,
      IDbTransaction trans
    )
    {
      using var cmd = _connection.CreateCommand();
      ((IDbCommand)cmd).Transaction = trans;
      cmd.CommandText =
        "SELECT c.CorrectionId, c.TargetObservationId, c.ReplacementObservationId, c.Note, s.TimeUTC "
        + "FROM basket_observation_corrections c JOIN stations s ON s.Counter = c.Counter WHERE c.Counter = $counter";
      cmd.Parameters.Add("counter", SqliteType.Integer).Value = counter;
      using var reader = cmd.ExecuteReader();
      if (!reader.Read())
        return null;
      var correctionId = Guid.Parse(reader.GetString(0));
      var targetObservationId = Guid.Parse(reader.GetString(1));
      var replacementObservationId = reader.IsDBNull(2)
        ? (Guid?)null
        : Guid.Parse(reader.GetString(2));
      var note = reader.IsDBNull(3) ? null : reader.GetString(3);
      var timeUTC = new DateTime(reader.GetInt64(4), DateTimeKind.Utc);
      reader.Close();
      var (source, correlationId) = BasketEvidenceSourceForCounter(counter, trans);
      return new BasketObservationCorrection
      {
        CorrectionId = correctionId,
        TargetObservationId = targetObservationId,
        ReplacementObservationId = replacementObservationId,
        Source = source,
        Note = note,
        CorrelationId = correlationId,
        TimeUTC = timeUTC,
        EventCounter = counter,
      };
    }

    private void InsertBasketEvidenceSource(
      long counter,
      BasketEvidenceSource source,
      IDbTransaction trans
    )
    {
      using var cmd = _connection.CreateCommand();
      ((IDbCommand)cmd).Transaction = trans;
      cmd.CommandText =
        "INSERT INTO basket_evidence_sources(Counter, SourceKind, SourceName) VALUES($counter, $kind, $name)";
      cmd.Parameters.Add("counter", SqliteType.Integer).Value = counter;
      cmd.Parameters.Add("kind", SqliteType.Integer).Value = (int)source.Kind;
      cmd.Parameters.Add("name", SqliteType.Text).Value = source.Name;
      cmd.ExecuteNonQuery();
    }

    private (BasketEvidenceSource Source, string CorrelationId) BasketEvidenceSourceForCounter(
      long counter,
      IDbTransaction trans
    )
    {
      using var cmd = _connection.CreateCommand();
      ((IDbCommand)cmd).Transaction = trans;
      cmd.CommandText =
        "SELECT SourceKind, SourceName FROM basket_evidence_sources WHERE Counter = $counter";
      cmd.Parameters.Add("counter", SqliteType.Integer).Value = counter;
      using var reader = cmd.ExecuteReader();
      if (!reader.Read())
        throw new InvalidOperationException(
          $"Basket evidence source for event {counter} is missing."
        );
      var source = new BasketEvidenceSource
      {
        Kind = (BasketEvidenceSourceKind)reader.GetInt32(0),
        Name = reader.GetString(1),
      };
      reader.Close();
      cmd.Parameters.Clear();
      cmd.CommandText = "SELECT CorrelationId FROM stations WHERE Counter = $counter";
      cmd.Parameters.Add("counter", SqliteType.Integer).Value = counter;
      return (source, cmd.ExecuteScalar() as string);
    }

    private LogEntry LogForCounter(long counter, IDbTransaction trans)
    {
      using var cmd = _connection.CreateCommand();
      ((IDbCommand)cmd).Transaction = trans;
      cmd.CommandText =
        "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName, BasketContentEpisodeId, ForeignID, CorrelationId FROM stations WHERE Counter = $counter";
      cmd.Parameters.Add("counter", SqliteType.Integer).Value = counter;
      using var reader = cmd.ExecuteReader();
      return LoadLog(reader, trans).Single();
    }

    private static BasketEvidenceSource NormalizeBasketEvidenceSource(BasketEvidenceSource source)
    {
      ArgumentNullException.ThrowIfNull(source);
      if (!Enum.IsDefined(source.Kind))
        throw new ArgumentOutOfRangeException(nameof(source), "Evidence source kind is invalid.");
      if (string.IsNullOrWhiteSpace(source.Name))
        throw new ArgumentException("Evidence source name is required.", nameof(source));
      return source with { Name = source.Name.Trim() };
    }

    private static BasketPosition NormalizeBasketPosition(BasketPosition position) =>
      position is null
        ? null
        : position with
        {
          LocationTitle = NormalizeOptional(position.LocationTitle),
        };

    private static string NormalizeOptional(string value) =>
      string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static EventLogMetadata NormalizeEventLogMetadata(EventLogMetadata metadata) =>
      (metadata ?? new EventLogMetadata()) with
      {
        ForeignId = NormalizeOptional(metadata?.ForeignId),
        CorrelationId = NormalizeOptional(metadata?.CorrelationId),
        OriginalMessage = NormalizeOptional(metadata?.OriginalMessage),
      };

    private static void ValidateBasketObservation(
      Guid observationId,
      int basketId,
      BasketPosition position,
      ImmutableSortedSet<Guid> contentEpisodeIds
    )
    {
      if (observationId == Guid.Empty)
        throw new ArgumentException("Observation ID can not be empty.", nameof(observationId));
      if (basketId <= 0)
        throw new ArgumentOutOfRangeException(nameof(basketId));
      ArgumentNullException.ThrowIfNull(position);
      if (position.LocationNum <= 0 || position.Zone is <= 0)
        throw new ArgumentException("Basket position numbers must be positive.", nameof(position));
      ArgumentNullException.ThrowIfNull(contentEpisodeIds);
      if (contentEpisodeIds.Contains(Guid.Empty))
        throw new ArgumentException(
          "Content episode IDs can not contain an empty UUID.",
          nameof(contentEpisodeIds)
        );
    }

    private static string BasketObservationFingerprint(
      int basketId,
      BasketPosition position,
      ImmutableSortedSet<Guid> contentEpisodeIds,
      BasketEvidenceSource source,
      string note
    )
    {
      var fingerprint = new StringBuilder();
      AppendFingerprint(fingerprint, basketId.ToString(CultureInfo.InvariantCulture));
      AppendBasketPositionFingerprint(fingerprint, position);
      foreach (var id in contentEpisodeIds)
        AppendFingerprint(fingerprint, id.ToString("D"));
      AppendBasketEvidenceSourceFingerprint(fingerprint, source);
      AppendFingerprint(fingerprint, note);
      return fingerprint.ToString();
    }

    private static string BasketObservationCorrectionFingerprint(
      Guid targetObservationId,
      BasketObservationReplacement replacement,
      BasketEvidenceSource source,
      string note
    )
    {
      var fingerprint = new StringBuilder();
      AppendFingerprint(fingerprint, targetObservationId.ToString("D"));
      if (replacement is null)
      {
        AppendFingerprint(fingerprint, null);
      }
      else
      {
        AppendFingerprint(fingerprint, replacement.ObservationId.ToString("D"));
        AppendFingerprint(fingerprint, replacement.BasketId.ToString(CultureInfo.InvariantCulture));
        AppendBasketPositionFingerprint(fingerprint, replacement.Position);
        foreach (var id in replacement.ContentEpisodeIds)
          AppendFingerprint(fingerprint, id.ToString("D"));
        AppendBasketEvidenceSourceFingerprint(fingerprint, replacement.Source);
      }
      AppendBasketEvidenceSourceFingerprint(fingerprint, source);
      AppendFingerprint(fingerprint, note);
      return fingerprint.ToString();
    }

    private static void AppendBasketEvidenceSourceFingerprint(
      StringBuilder fingerprint,
      BasketEvidenceSource source
    )
    {
      AppendFingerprint(fingerprint, source.Kind.ToString());
      AppendFingerprint(fingerprint, source.Name);
    }

    private static void AppendBasketPositionFingerprint(
      StringBuilder fingerprint,
      BasketPosition position
    )
    {
      if (position is null)
      {
        AppendFingerprint(fingerprint, null);
        return;
      }
      AppendFingerprint(fingerprint, position.Location.ToString());
      AppendFingerprint(fingerprint, position.LocationNum.ToString(CultureInfo.InvariantCulture));
      AppendFingerprint(fingerprint, position.Zone?.ToString(CultureInfo.InvariantCulture));
      AppendFingerprint(fingerprint, position.LocationTitle);
    }
  }
}
