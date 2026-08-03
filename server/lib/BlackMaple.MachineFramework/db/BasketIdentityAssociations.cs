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
    private sealed record AddedBasketIdentityAssociation(
      BasketIdentityAssociation Association,
      LogEntry Log,
      bool Created
    );

    public BasketIdentityAssociation RecordBasketIdentityAssociation(
      Guid associationId,
      int basketId,
      ImmutableSortedSet<Guid> contentEpisodeIds,
      BasketIdentityAssociationBasis basis,
      BasketEvidenceSource source,
      DateTime timeUTC,
      BasketPosition observedPosition = null,
      EventLogMetadata metadata = null,
      string note = null
    )
    {
      var normalizedSource = NormalizeBasketEvidenceSource(source);
      var normalizedMetadata = NormalizeEventLogMetadata(metadata);
      var normalizedPosition = NormalizeBasketPosition(observedPosition);
      var normalizedNote = NormalizeOptional(note);
      ValidateBasketIdentityAssociation(
        associationId,
        basketId,
        contentEpisodeIds,
        basis,
        normalizedPosition
      );
      var fingerprint = BasketIdentityAssociationFingerprint(
        basketId,
        contentEpisodeIds,
        basis,
        normalizedSource,
        normalizedPosition,
        normalizedNote
      );

      AddedBasketIdentityAssociation added;
      lock (_cfg)
      {
        using var trans = _connection.BeginTransaction();
        added = AddBasketIdentityAssociation(
          associationId,
          basketId,
          contentEpisodeIds,
          basis,
          normalizedSource,
          timeUTC,
          normalizedPosition,
          normalizedMetadata,
          trans,
          fingerprint,
          normalizedNote
        );
        trans.Commit();
      }
      if (added.Created)
        _cfg.OnNewLogEntry(added.Log, normalizedMetadata.ForeignId, this);
      return added.Association;
    }

    public BasketIdentityAssociationCorrectionResult CorrectBasketIdentityAssociation(
      Guid correctionId,
      Guid targetAssociationId,
      [AllowNull] BasketIdentityAssociationReplacement replacement,
      BasketEvidenceSource source,
      DateTime timeUTC,
      string note = null,
      EventLogMetadata metadata = null
    )
    {
      if (correctionId == Guid.Empty)
        throw new ArgumentException("Correction ID can not be empty.", nameof(correctionId));
      if (targetAssociationId == Guid.Empty)
        throw new ArgumentException(
          "Target association ID can not be empty.",
          nameof(targetAssociationId)
        );
      if (replacement?.AssociationId == targetAssociationId)
        throw new ArgumentException(
          "The replacement association must have a new association ID.",
          nameof(replacement)
        );

      var normalizedSource = NormalizeBasketEvidenceSource(source);
      var normalizedMetadata = NormalizeEventLogMetadata(metadata);
      var normalizedNote = NormalizeOptional(note);
      var normalizedReplacement = replacement is null
        ? null
        : replacement with
        {
          Source = NormalizeBasketEvidenceSource(replacement.Source),
          ObservedPosition = NormalizeBasketPosition(replacement.ObservedPosition),
        };
      if (normalizedReplacement is not null)
        ValidateBasketIdentityAssociation(
          normalizedReplacement.AssociationId,
          normalizedReplacement.BasketId,
          normalizedReplacement.ContentEpisodeIds,
          normalizedReplacement.Basis,
          normalizedReplacement.ObservedPosition
        );
      var fingerprint = BasketIdentityAssociationCorrectionFingerprint(
        targetAssociationId,
        normalizedReplacement,
        normalizedSource,
        normalizedNote
      );

      BasketIdentityAssociationCorrectionResult result;
      var newLogs = ImmutableList.CreateBuilder<LogEntry>();
      lock (_cfg)
      {
        using var trans = _connection.BeginTransaction();
        using (var existing = _connection.CreateCommand())
        {
          existing.Transaction = trans;
          existing.CommandText =
            "SELECT Fingerprint FROM basket_identity_association_corrections WHERE CorrectionId = $id";
          existing.Parameters.Add("id", SqliteType.Text).Value = correctionId.ToString("D");
          var existingFingerprint = existing.ExecuteScalar() as string;
          if (existingFingerprint is not null)
          {
            if (existingFingerprint != fingerprint)
              throw new ConflictRequestException(
                $"Basket identity association correction {correctionId:D} was already used with different arguments."
              );
            result = BasketIdentityAssociationCorrectionResultForId(correctionId, trans);
            trans.Commit();
            return result;
          }
        }

        var target = BasketIdentityAssociationForId(targetAssociationId, trans);
        if (target is null)
          throw new ConflictRequestException(
            $"Basket identity association {targetAssociationId:D} does not exist."
          );
        using (var current = _connection.CreateCommand())
        {
          current.Transaction = trans;
          current.CommandText =
            "SELECT SupersededByCorrectionId FROM basket_identity_associations WHERE AssociationId = $id";
          current.Parameters.Add("id", SqliteType.Text).Value = targetAssociationId.ToString("D");
          var superseded = current.ExecuteScalar();
          if (superseded is not null and not DBNull)
            throw new ConflictRequestException(
              $"Basket identity association {targetAssociationId:D} is already superseded."
            );
        }

        if (normalizedReplacement is not null)
        {
          using var replacementExists = _connection.CreateCommand();
          replacementExists.Transaction = trans;
          replacementExists.CommandText =
            "SELECT 1 FROM basket_identity_associations WHERE AssociationId = $id";
          replacementExists.Parameters.Add("id", SqliteType.Text).Value =
            normalizedReplacement.AssociationId.ToString("D");
          if (replacementExists.ExecuteScalar() is not null)
            throw new ConflictRequestException(
              $"Basket identity association {normalizedReplacement.AssociationId:D} already exists."
            );
        }

        var correctionEntry = new NewEventLogEntry
        {
          Material = [],
          Pallet = target.BasketId,
          LogType = LogType.BasketIdentityAssociationCorrection,
          LocationName = "Basket Identity",
          LocationNum = 1,
          Program = "Association Correction",
          StartOfCycle = false,
          EndTimeUTC = timeUTC,
          Result = normalizedReplacement is null ? "Retracted" : "Replaced",
          ElapsedTime = TimeSpan.Zero,
          ActiveOperationTime = TimeSpan.Zero,
          Metadata = normalizedMetadata,
        };
        correctionEntry.ProgramDetails.Add("sourceKind", normalizedSource.Kind.ToString());
        correctionEntry.ProgramDetails.Add("sourceName", normalizedSource.Name);
        correctionEntry.ProgramDetails.Add(
          "episodeCount",
          target.ContentEpisodeIds.Count.ToString(CultureInfo.InvariantCulture)
        );
        if (normalizedNote is not null)
          correctionEntry.ProgramDetails.Add("note", normalizedNote);
        var correctionLog = AddLogEntry(trans, correctionEntry, normalizedMetadata);
        InsertBasketEvidenceSource(correctionLog.Counter, normalizedSource, trans);
        using (var insertCorrection = _connection.CreateCommand())
        {
          insertCorrection.Transaction = trans;
          insertCorrection.CommandText =
            "INSERT INTO basket_identity_association_corrections(CorrectionId, Fingerprint, TargetAssociationId, ReplacementAssociationId, Counter, Note) VALUES($id, $fingerprint, $target, $replacement, $counter, $note)";
          insertCorrection.Parameters.Add("id", SqliteType.Text).Value = correctionId.ToString("D");
          insertCorrection.Parameters.Add("fingerprint", SqliteType.Text).Value = fingerprint;
          insertCorrection.Parameters.Add("target", SqliteType.Text).Value =
            targetAssociationId.ToString("D");
          insertCorrection.Parameters.Add("replacement", SqliteType.Text).Value =
            normalizedReplacement is null
              ? DBNull.Value
              : normalizedReplacement.AssociationId.ToString("D");
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
            "UPDATE basket_identity_associations SET SupersededByCorrectionId = $correction WHERE AssociationId = $target";
          supersede.Parameters.Add("correction", SqliteType.Text).Value = correctionId.ToString(
            "D"
          );
          supersede.Parameters.Add("target", SqliteType.Text).Value = targetAssociationId.ToString(
            "D"
          );
          supersede.ExecuteNonQuery();
          supersede.CommandText =
            "DELETE FROM current_basket_identity_associations WHERE AssociationCounter = $counter";
          supersede.Parameters.Clear();
          supersede.Parameters.Add("counter", SqliteType.Integer).Value = target.EventCounter;
          supersede.ExecuteNonQuery();
        }

        BasketIdentityAssociation replacementAssociation = null;
        newLogs.Add(correctionLog);
        if (normalizedReplacement is not null)
        {
          var added = AddBasketIdentityAssociation(
            normalizedReplacement.AssociationId,
            normalizedReplacement.BasketId,
            normalizedReplacement.ContentEpisodeIds,
            normalizedReplacement.Basis,
            normalizedReplacement.Source,
            timeUTC,
            normalizedReplacement.ObservedPosition,
            normalizedMetadata,
            trans
          );
          replacementAssociation = added.Association;
          newLogs.Add(added.Log);
        }

        result = new BasketIdentityAssociationCorrectionResult
        {
          Correction = new BasketIdentityAssociationCorrection
          {
            CorrectionId = correctionId,
            TargetAssociationId = targetAssociationId,
            ReplacementAssociationId = normalizedReplacement?.AssociationId,
            Source = normalizedSource,
            Note = normalizedNote,
            CorrelationId = normalizedMetadata.CorrelationId,
            TimeUTC = timeUTC,
            EventCounter = correctionLog.Counter,
          },
          Replacement = replacementAssociation,
        };
        trans.Commit();
      }

      foreach (var log in newLogs)
        _cfg.OnNewLogEntry(log, normalizedMetadata.ForeignId, this);
      return result;
    }

    public ImmutableList<BasketIdentityAssociation> GetCurrentBasketIdentityAssociations(
      int? basketNum = null
    )
    {
      using var trans = _connection.BeginTransaction();
      using var cmd = _connection.CreateCommand();
      cmd.Transaction = trans;
      cmd.CommandText =
        "SELECT DISTINCT AssociationCounter FROM current_basket_identity_associations "
        + (basketNum.HasValue ? "WHERE BasketNum = $num " : "")
        + "ORDER BY BasketNum, AssociationCounter";
      if (basketNum.HasValue)
        cmd.Parameters.Add("num", SqliteType.Integer).Value = basketNum.Value;
      using var reader = cmd.ExecuteReader();
      var counters = ImmutableList.CreateBuilder<long>();
      while (reader.Read())
        counters.Add(reader.GetInt64(0));
      var associations = counters
        .Select(counter => CurrentBasketIdentityAssociationForCounter(counter, trans))
        .ToImmutableList();
      trans.Commit();
      return associations;
    }

    [return: MaybeNull]
    public BasketIdentityAssociation GetBasketIdentityAssociation(Guid associationId)
    {
      if (associationId == Guid.Empty)
        throw new ArgumentException("Association ID can not be empty.", nameof(associationId));
      using var trans = _connection.BeginTransaction();
      var association = BasketIdentityAssociationForId(associationId, trans);
      trans.Commit();
      return association;
    }

    public ImmutableList<BasketIdentityAssociationCorrection> GetBasketIdentityAssociationCorrections(
      Guid? targetAssociationId = null
    )
    {
      using var trans = _connection.BeginTransaction();
      using var cmd = _connection.CreateCommand();
      cmd.Transaction = trans;
      cmd.CommandText =
        "SELECT Counter FROM basket_identity_association_corrections "
        + (targetAssociationId.HasValue ? "WHERE TargetAssociationId = $target " : "")
        + "ORDER BY Counter";
      if (targetAssociationId.HasValue)
        cmd.Parameters.Add("target", SqliteType.Text).Value = targetAssociationId.Value.ToString(
          "D"
        );
      using var reader = cmd.ExecuteReader();
      var counters = ImmutableList.CreateBuilder<long>();
      while (reader.Read())
        counters.Add(reader.GetInt64(0));
      var corrections = counters
        .Select(counter => BasketIdentityAssociationCorrectionForCounter(counter, trans))
        .ToImmutableList();
      trans.Commit();
      return corrections;
    }

    private AddedBasketIdentityAssociation AddBasketIdentityAssociation(
      Guid associationId,
      int basketId,
      ImmutableSortedSet<Guid> contentEpisodeIds,
      BasketIdentityAssociationBasis basis,
      BasketEvidenceSource source,
      DateTime timeUTC,
      BasketPosition observedPosition,
      EventLogMetadata metadata,
      IDbTransaction trans,
      string fingerprint = null,
      string note = null
    )
    {
      fingerprint ??= BasketIdentityAssociationFingerprint(
        basketId,
        contentEpisodeIds,
        basis,
        source,
        observedPosition,
        note
      );
      using (var existing = _connection.CreateCommand())
      {
        ((IDbCommand)existing).Transaction = trans;
        existing.CommandText =
          "SELECT Fingerprint, Counter FROM basket_identity_associations WHERE AssociationId = $id";
        existing.Parameters.Add("id", SqliteType.Text).Value = associationId.ToString("D");
        using var reader = existing.ExecuteReader();
        if (reader.Read())
        {
          if (reader.GetString(0) != fingerprint)
            throw new ConflictRequestException(
              $"Basket identity association {associationId:D} was already used with different arguments."
            );
          var counter = reader.GetInt64(1);
          return new AddedBasketIdentityAssociation(
            BasketIdentityAssociationForCounter(counter, trans),
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
          "SELECT BasketNum FROM current_basket_identity_associations WHERE ContentEpisodeId = $id";
        current.Parameters.Add("id", SqliteType.Text).Value = contentEpisodeId.ToString("D");
        if (current.ExecuteScalar() is { } existingBasket)
          throw new ConflictRequestException(
            $"Basket content episode {contentEpisodeId:D} is already associated with basket {Convert.ToInt32(existingBasket, CultureInfo.InvariantCulture)}."
          );
      }

      var newLog = new NewEventLogEntry
      {
        Material = [],
        Pallet = basketId,
        LogType = LogType.BasketIdentityAssociation,
        LocationName = observedPosition?.LocationTitle ?? "Basket Identity",
        LocationNum = observedPosition?.LocationNum ?? 1,
        Program = "Association",
        StartOfCycle = false,
        EndTimeUTC = timeUTC,
        Result = basis.ToString(),
        ElapsedTime = TimeSpan.Zero,
        ActiveOperationTime = TimeSpan.Zero,
        Metadata = metadata,
      };
      newLog.ProgramDetails.Add("sourceKind", source.Kind.ToString());
      newLog.ProgramDetails.Add("sourceName", source.Name);
      newLog.ProgramDetails.Add("basis", basis.ToString());
      if (observedPosition is { } position)
      {
        newLog.ProgramDetails.Add("location", position.Location.ToString());
        if (position.Zone is { } zone)
          newLog.ProgramDetails.Add("zone", zone.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(position.LocationTitle))
          newLog.ProgramDetails.Add("locationTitle", position.LocationTitle);
      }
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
          "INSERT INTO basket_identity_associations(AssociationId, Fingerprint, Counter, SupersededByCorrectionId) VALUES($id, $fingerprint, $counter, NULL)";
        insert.Parameters.Add("id", SqliteType.Text).Value = associationId.ToString("D");
        insert.Parameters.Add("fingerprint", SqliteType.Text).Value = fingerprint;
        insert.Parameters.Add("counter", SqliteType.Integer).Value = log.Counter;
        insert.ExecuteNonQuery();

        insert.CommandText =
          "INSERT INTO basket_identity_association_details(Counter, Basis, ObservedLocation, ObservedLocationNum, ObservedZone, ObservedLocationTitle, Note) VALUES($counter, $basis, $location, $locationNum, $zone, $title, $note)";
        insert.Parameters.Clear();
        insert.Parameters.Add("counter", SqliteType.Integer).Value = log.Counter;
        insert.Parameters.Add("basis", SqliteType.Integer).Value = (int)basis;
        insert.Parameters.Add("location", SqliteType.Integer).Value = observedPosition is null
          ? DBNull.Value
          : (int)observedPosition.Location;
        insert.Parameters.Add("locationNum", SqliteType.Integer).Value = observedPosition is null
          ? DBNull.Value
          : observedPosition.LocationNum;
        insert.Parameters.Add("zone", SqliteType.Integer).Value = observedPosition?.Zone is { } zone
          ? zone
          : DBNull.Value;
        insert.Parameters.Add("title", SqliteType.Text).Value = observedPosition?.LocationTitle
          is { } title
          ? title
          : DBNull.Value;
        insert.Parameters.Add("note", SqliteType.Text).Value = note is { } value
          ? value
          : DBNull.Value;
        insert.ExecuteNonQuery();

        foreach (var contentEpisodeId in contentEpisodeIds)
        {
          insert.CommandText =
            "INSERT INTO basket_identity_association_episodes(Counter, ContentEpisodeId) VALUES($counter, $episode)";
          insert.Parameters.Clear();
          insert.Parameters.Add("counter", SqliteType.Integer).Value = log.Counter;
          insert.Parameters.Add("episode", SqliteType.Text).Value = contentEpisodeId.ToString("D");
          insert.ExecuteNonQuery();
          insert.CommandText =
            "INSERT INTO current_basket_identity_associations(ContentEpisodeId, AssociationCounter, BasketNum) VALUES($episode, $counter, $basket)";
          insert.Parameters.Clear();
          insert.Parameters.Add("episode", SqliteType.Text).Value = contentEpisodeId.ToString("D");
          insert.Parameters.Add("counter", SqliteType.Integer).Value = log.Counter;
          insert.Parameters.Add("basket", SqliteType.Integer).Value = basketId;
          insert.ExecuteNonQuery();
        }
      }

      return new AddedBasketIdentityAssociation(
        new BasketIdentityAssociation
        {
          AssociationId = associationId,
          BasketId = basketId,
          ContentEpisodeIds = contentEpisodeIds,
          Basis = basis,
          Source = source,
          Note = note,
          ObservedPosition = observedPosition,
          CorrelationId = metadata.CorrelationId,
          TimeUTC = timeUTC,
          EventCounter = log.Counter,
        },
        log,
        Created: true
      );
    }

    private BasketIdentityAssociation BasketIdentityAssociationForId(
      Guid associationId,
      IDbTransaction trans
    )
    {
      using var cmd = _connection.CreateCommand();
      ((IDbCommand)cmd).Transaction = trans;
      cmd.CommandText =
        "SELECT Counter FROM basket_identity_associations WHERE AssociationId = $id";
      cmd.Parameters.Add("id", SqliteType.Text).Value = associationId.ToString("D");
      return cmd.ExecuteScalar() is long counter
        ? BasketIdentityAssociationForCounter(counter, trans)
        : null;
    }

    private BasketIdentityAssociation BasketIdentityAssociationForCounter(
      long counter,
      IDbTransaction trans
    )
    {
      using var cmd = _connection.CreateCommand();
      ((IDbCommand)cmd).Transaction = trans;
      cmd.CommandText =
        "SELECT a.AssociationId, s.Pallet, d.Basis, d.ObservedLocation, d.ObservedLocationNum, d.ObservedZone, d.ObservedLocationTitle, d.Note, s.TimeUTC "
        + "FROM basket_identity_associations a JOIN stations s ON s.Counter = a.Counter "
        + "JOIN basket_identity_association_details d ON d.Counter = a.Counter WHERE a.Counter = $counter";
      cmd.Parameters.Add("counter", SqliteType.Integer).Value = counter;
      using var reader = cmd.ExecuteReader();
      if (!reader.Read())
        return null;
      var associationId = Guid.Parse(reader.GetString(0));
      var basketId = reader.GetInt32(1);
      var basis = (BasketIdentityAssociationBasis)reader.GetInt32(2);
      var observedPosition = reader.IsDBNull(3)
        ? null
        : new BasketPosition
        {
          Location = (BasketLocationEnum)reader.GetInt32(3),
          LocationNum = reader.GetInt32(4),
          Zone = reader.IsDBNull(5) ? null : reader.GetInt32(5),
          LocationTitle = reader.IsDBNull(6) ? null : reader.GetString(6),
        };
      var note = reader.IsDBNull(7) ? null : reader.GetString(7);
      var timeUTC = new DateTime(reader.GetInt64(8), DateTimeKind.Utc);
      reader.Close();

      cmd.CommandText =
        "SELECT ContentEpisodeId FROM basket_identity_association_episodes WHERE Counter = $counter ORDER BY ContentEpisodeId";
      using var episodeReader = cmd.ExecuteReader();
      var episodes = ImmutableSortedSet.CreateBuilder<Guid>();
      while (episodeReader.Read())
        episodes.Add(Guid.Parse(episodeReader.GetString(0)));
      episodeReader.Close();
      var (source, correlationId) = BasketEvidenceSourceForCounter(counter, trans);
      return new BasketIdentityAssociation
      {
        AssociationId = associationId,
        BasketId = basketId,
        ContentEpisodeIds = episodes.ToImmutable(),
        Basis = basis,
        Source = source,
        Note = note,
        ObservedPosition = observedPosition,
        CorrelationId = correlationId,
        TimeUTC = timeUTC,
        EventCounter = counter,
      };
    }

    private BasketIdentityAssociation CurrentBasketIdentityAssociationForCounter(
      long counter,
      IDbTransaction trans
    )
    {
      var association = BasketIdentityAssociationForCounter(counter, trans);
      using var cmd = _connection.CreateCommand();
      ((IDbCommand)cmd).Transaction = trans;
      cmd.CommandText =
        "SELECT ContentEpisodeId FROM current_basket_identity_associations WHERE AssociationCounter = $counter ORDER BY ContentEpisodeId";
      cmd.Parameters.Add("counter", SqliteType.Integer).Value = counter;
      using var reader = cmd.ExecuteReader();
      var activeEpisodes = ImmutableSortedSet.CreateBuilder<Guid>();
      while (reader.Read())
        activeEpisodes.Add(Guid.Parse(reader.GetString(0)));
      return association with { ContentEpisodeIds = activeEpisodes.ToImmutable() };
    }

    private BasketIdentityAssociationCorrectionResult BasketIdentityAssociationCorrectionResultForId(
      Guid correctionId,
      IDbTransaction trans
    )
    {
      using var cmd = _connection.CreateCommand();
      ((IDbCommand)cmd).Transaction = trans;
      cmd.CommandText =
        "SELECT Counter, ReplacementAssociationId FROM basket_identity_association_corrections WHERE CorrectionId = $id";
      cmd.Parameters.Add("id", SqliteType.Text).Value = correctionId.ToString("D");
      using var reader = cmd.ExecuteReader();
      if (!reader.Read())
        return null;
      var counter = reader.GetInt64(0);
      var replacementId = reader.IsDBNull(1) ? (Guid?)null : Guid.Parse(reader.GetString(1));
      reader.Close();
      return new BasketIdentityAssociationCorrectionResult
      {
        Correction = BasketIdentityAssociationCorrectionForCounter(counter, trans),
        Replacement = replacementId is { } id ? BasketIdentityAssociationForId(id, trans) : null,
      };
    }

    private BasketIdentityAssociationCorrection BasketIdentityAssociationCorrectionForCounter(
      long counter,
      IDbTransaction trans
    )
    {
      using var cmd = _connection.CreateCommand();
      ((IDbCommand)cmd).Transaction = trans;
      cmd.CommandText =
        "SELECT c.CorrectionId, c.TargetAssociationId, c.ReplacementAssociationId, c.Note, s.TimeUTC "
        + "FROM basket_identity_association_corrections c JOIN stations s ON s.Counter = c.Counter WHERE c.Counter = $counter";
      cmd.Parameters.Add("counter", SqliteType.Integer).Value = counter;
      using var reader = cmd.ExecuteReader();
      if (!reader.Read())
        return null;
      var correctionId = Guid.Parse(reader.GetString(0));
      var targetAssociationId = Guid.Parse(reader.GetString(1));
      var replacementAssociationId = reader.IsDBNull(2)
        ? (Guid?)null
        : Guid.Parse(reader.GetString(2));
      var note = reader.IsDBNull(3) ? null : reader.GetString(3);
      var timeUTC = new DateTime(reader.GetInt64(4), DateTimeKind.Utc);
      reader.Close();
      var (source, correlationId) = BasketEvidenceSourceForCounter(counter, trans);
      return new BasketIdentityAssociationCorrection
      {
        CorrectionId = correctionId,
        TargetAssociationId = targetAssociationId,
        ReplacementAssociationId = replacementAssociationId,
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
      var correlationId = cmd.ExecuteScalar() as string;
      return (source, correlationId);
    }

    private LogEntry LogForCounter(long counter, IDbTransaction trans)
    {
      using var cmd = _connection.CreateCommand();
      ((IDbCommand)cmd).Transaction = trans;
      cmd.CommandText =
        "SELECT Counter, Pallet, StationLoc, StationNum, Program, Start, TimeUTC, Result, EndOfRoute, Elapsed, ActiveTime, StationName, ContainerId, ForeignID, CorrelationId FROM stations WHERE Counter = $counter";
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

    private static void ValidateBasketIdentityAssociation(
      Guid associationId,
      int basketId,
      ImmutableSortedSet<Guid> contentEpisodeIds,
      BasketIdentityAssociationBasis basis,
      BasketPosition observedPosition
    )
    {
      if (associationId == Guid.Empty)
        throw new ArgumentException("Association ID can not be empty.", nameof(associationId));
      if (basketId <= 0)
        throw new ArgumentOutOfRangeException(nameof(basketId));
      ArgumentNullException.ThrowIfNull(contentEpisodeIds);
      if (contentEpisodeIds.IsEmpty || contentEpisodeIds.Contains(Guid.Empty))
        throw new ArgumentException(
          "Content episode IDs must be nonempty and contain no empty UUID.",
          nameof(contentEpisodeIds)
        );
      if (!Enum.IsDefined(basis))
        throw new ArgumentOutOfRangeException(nameof(basis));
      if (
        observedPosition is not null
        && (observedPosition.LocationNum <= 0 || observedPosition.Zone is <= 0)
      )
        throw new ArgumentException(
          "Observed basket position numbers must be positive.",
          nameof(observedPosition)
        );
    }

    private static string BasketIdentityAssociationFingerprint(
      int basketId,
      ImmutableSortedSet<Guid> contentEpisodeIds,
      BasketIdentityAssociationBasis basis,
      BasketEvidenceSource source,
      BasketPosition observedPosition,
      string note
    )
    {
      var fingerprint = new StringBuilder();
      AppendFingerprint(fingerprint, basketId.ToString(CultureInfo.InvariantCulture));
      foreach (var id in contentEpisodeIds)
        AppendFingerprint(fingerprint, id.ToString("D"));
      AppendFingerprint(fingerprint, basis.ToString());
      AppendBasketEvidenceSourceFingerprint(fingerprint, source);
      AppendBasketPositionFingerprint(fingerprint, observedPosition);
      AppendFingerprint(fingerprint, note);
      return fingerprint.ToString();
    }

    private static string BasketIdentityAssociationCorrectionFingerprint(
      Guid targetAssociationId,
      BasketIdentityAssociationReplacement replacement,
      BasketEvidenceSource source,
      string note
    )
    {
      var fingerprint = new StringBuilder();
      AppendFingerprint(fingerprint, targetAssociationId.ToString("D"));
      if (replacement is null)
      {
        AppendFingerprint(fingerprint, null);
      }
      else
      {
        AppendFingerprint(fingerprint, replacement.AssociationId.ToString("D"));
        AppendFingerprint(fingerprint, replacement.BasketId.ToString(CultureInfo.InvariantCulture));
        foreach (var id in replacement.ContentEpisodeIds)
          AppendFingerprint(fingerprint, id.ToString("D"));
        AppendFingerprint(fingerprint, replacement.Basis.ToString());
        AppendBasketEvidenceSourceFingerprint(fingerprint, replacement.Source);
        AppendBasketPositionFingerprint(fingerprint, replacement.ObservedPosition);
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
      AppendFingerprint(fingerprint, position?.Location.ToString());
      AppendFingerprint(fingerprint, position?.LocationNum.ToString(CultureInfo.InvariantCulture));
      AppendFingerprint(fingerprint, position?.Zone?.ToString(CultureInfo.InvariantCulture));
      AppendFingerprint(fingerprint, position?.LocationTitle);
    }
  }
}
