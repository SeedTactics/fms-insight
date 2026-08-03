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
    public BasketRegionSurvey RecordBasketRegionSurvey(
      Guid surveyId,
      BasketPosition region,
      ImmutableSortedSet<int> observedBasketIds,
      int unidentifiedBasketCount,
      BasketRegionSurveyCompleteness completeness,
      DateTime timeUTC,
      BasketEvidenceSource source,
      EventLogMetadata metadata = null
    )
    {
      var normalizedRegion = NormalizeBasketPosition(region);
      var normalizedSource = NormalizeBasketEvidenceSource(source);
      var normalizedMetadata = NormalizeEventLogMetadata(metadata);
      ValidateBasketRegionSurvey(
        surveyId,
        normalizedRegion,
        observedBasketIds,
        unidentifiedBasketCount,
        completeness
      );
      var fingerprint = BasketRegionSurveyFingerprint(
        normalizedRegion,
        observedBasketIds,
        unidentifiedBasketCount,
        completeness,
        normalizedSource
      );

      BasketRegionSurvey survey;
      LogEntry log = null;
      lock (_cfg)
      {
        using var trans = _connection.BeginTransaction();
        using (var existing = _connection.CreateCommand())
        {
          existing.Transaction = trans;
          existing.CommandText =
            "SELECT Fingerprint, Counter FROM basket_region_surveys WHERE SurveyId = $id";
          existing.Parameters.Add("id", SqliteType.Text).Value = surveyId.ToString("D");
          using var reader = existing.ExecuteReader();
          if (reader.Read())
          {
            if (reader.GetString(0) != fingerprint)
              throw new ConflictRequestException(
                $"Basket region survey {surveyId:D} was already used with different arguments."
              );
            survey = BasketRegionSurveyForCounter(reader.GetInt64(1), trans);
            trans.Commit();
            return survey;
          }
        }

        var newLog = new NewEventLogEntry
        {
          Material = [],
          Pallet = 0,
          LogType = LogType.BasketRegionSurvey,
          LocationName = normalizedRegion.LocationTitle ?? normalizedRegion.Location.ToString(),
          LocationNum = normalizedRegion.LocationNum,
          Program = normalizedSource.Kind.ToString(),
          StartOfCycle = false,
          EndTimeUTC = timeUTC,
          Result = completeness.ToString(),
          ElapsedTime = TimeSpan.Zero,
          ActiveOperationTime = TimeSpan.Zero,
          Metadata = normalizedMetadata,
        };
        newLog.ProgramDetails.Add("sourceKind", normalizedSource.Kind.ToString());
        newLog.ProgramDetails.Add("sourceName", normalizedSource.Name);
        newLog.ProgramDetails.Add("location", normalizedRegion.Location.ToString());
        if (normalizedRegion.Zone is { } regionZone)
          newLog.ProgramDetails.Add("zone", regionZone.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(normalizedRegion.LocationTitle))
          newLog.ProgramDetails.Add("locationTitle", normalizedRegion.LocationTitle);
        newLog.ProgramDetails.Add(
          "observedBasketCount",
          observedBasketIds.Count.ToString(CultureInfo.InvariantCulture)
        );
        newLog.ProgramDetails.Add(
          "unidentifiedBasketCount",
          unidentifiedBasketCount.ToString(CultureInfo.InvariantCulture)
        );
        log = AddLogEntry(trans, newLog, normalizedMetadata);
        InsertBasketEvidenceSource(log.Counter, normalizedSource, trans);
        using (var insert = _connection.CreateCommand())
        {
          insert.Transaction = trans;
          insert.CommandText =
            "INSERT INTO basket_region_surveys(SurveyId, Fingerprint, Counter) VALUES($id, $fingerprint, $counter)";
          insert.Parameters.Add("id", SqliteType.Text).Value = surveyId.ToString("D");
          insert.Parameters.Add("fingerprint", SqliteType.Text).Value = fingerprint;
          insert.Parameters.Add("counter", SqliteType.Integer).Value = log.Counter;
          insert.ExecuteNonQuery();

          insert.CommandText =
            "INSERT INTO basket_region_survey_details(Counter, RegionLocation, RegionZone, RegionTitle, UnidentifiedBasketCount, Completeness) VALUES($counter, $location, $zone, $title, $unidentified, $completeness)";
          insert.Parameters.Clear();
          insert.Parameters.Add("counter", SqliteType.Integer).Value = log.Counter;
          insert.Parameters.Add("location", SqliteType.Integer).Value = (int)
            normalizedRegion.Location;
          insert.Parameters.Add("zone", SqliteType.Integer).Value = normalizedRegion.Zone
            is { } zone
            ? zone
            : DBNull.Value;
          insert.Parameters.Add("title", SqliteType.Text).Value = normalizedRegion.LocationTitle
            is { } title
            ? title
            : DBNull.Value;
          insert.Parameters.Add("unidentified", SqliteType.Integer).Value = unidentifiedBasketCount;
          insert.Parameters.Add("completeness", SqliteType.Integer).Value = (int)completeness;
          insert.ExecuteNonQuery();

          foreach (var basketId in observedBasketIds)
          {
            insert.CommandText =
              "INSERT INTO basket_region_survey_baskets(Counter, BasketId) VALUES($counter, $basket)";
            insert.Parameters.Clear();
            insert.Parameters.Add("counter", SqliteType.Integer).Value = log.Counter;
            insert.Parameters.Add("basket", SqliteType.Integer).Value = basketId;
            insert.ExecuteNonQuery();
          }
        }

        survey = new BasketRegionSurvey
        {
          SurveyId = surveyId,
          Region = normalizedRegion,
          ObservedBasketIds = observedBasketIds,
          UnidentifiedBasketCount = unidentifiedBasketCount,
          Completeness = completeness,
          Source = normalizedSource,
          CorrelationId = normalizedMetadata.CorrelationId,
          TimeUTC = timeUTC,
          EventCounter = log.Counter,
        };
        trans.Commit();
      }
      _cfg.OnNewLogEntry(log, normalizedMetadata.ForeignId, this);
      return survey;
    }

    public ImmutableList<BasketRegionSurvey> GetBasketRegionSurveys(
      BasketPosition region = null,
      long? afterCounter = null
    )
    {
      var normalizedRegion = NormalizeBasketPosition(region);
      if (normalizedRegion is not null)
        ValidateBasketRegion(normalizedRegion);
      using var trans = _connection.BeginTransaction();
      using var cmd = _connection.CreateCommand();
      cmd.Transaction = trans;
      cmd.CommandText =
        "SELECT Counter FROM basket_region_surveys "
        + (afterCounter.HasValue ? "WHERE Counter > $after " : "")
        + "ORDER BY Counter";
      if (afterCounter.HasValue)
        cmd.Parameters.Add("after", SqliteType.Integer).Value = afterCounter.Value;
      using var reader = cmd.ExecuteReader();
      var counters = ImmutableList.CreateBuilder<long>();
      while (reader.Read())
        counters.Add(reader.GetInt64(0));
      var surveys = counters
        .Select(counter => BasketRegionSurveyForCounter(counter, trans))
        .Where(survey =>
          normalizedRegion is null || SameBasketRegion(survey.Region, normalizedRegion)
        )
        .ToImmutableList();
      trans.Commit();
      return surveys;
    }

    [return: MaybeNull]
    public BasketRegionSurvey GetBasketRegionSurvey(Guid surveyId)
    {
      if (surveyId == Guid.Empty)
        throw new ArgumentException("Survey ID can not be empty.", nameof(surveyId));
      using var trans = _connection.BeginTransaction();
      using var cmd = _connection.CreateCommand();
      cmd.Transaction = trans;
      cmd.CommandText = "SELECT Counter FROM basket_region_surveys WHERE SurveyId = $id";
      cmd.Parameters.Add("id", SqliteType.Text).Value = surveyId.ToString("D");
      var survey = cmd.ExecuteScalar() is long counter
        ? BasketRegionSurveyForCounter(counter, trans)
        : null;
      trans.Commit();
      return survey;
    }

    public ImmutableList<BasketRegionSurvey> GetLatestBasketRegionSurveys() =>
      GetBasketRegionSurveys()
        .GroupBy(survey => BasketRegionKey(survey.Region))
        .Select(group => group.MaxBy(survey => survey.EventCounter))
        .OrderBy(survey => survey.Region.Location)
        .ThenBy(survey => survey.Region.LocationNum)
        .ThenBy(survey => survey.Region.Zone)
        .ToImmutableList();

    private static bool SameBasketRegion(BasketPosition left, BasketPosition right) =>
      left.Location == right.Location
      && left.LocationNum == right.LocationNum
      && left.Zone == right.Zone;

    private static (BasketLocationEnum Location, int LocationNum, int? Zone) BasketRegionKey(
      BasketPosition position
    ) => (position.Location, position.LocationNum, position.Zone);

    private BasketRegionSurvey BasketRegionSurveyForCounter(long counter, IDbTransaction trans)
    {
      using var cmd = _connection.CreateCommand();
      ((IDbCommand)cmd).Transaction = trans;
      cmd.CommandText =
        "SELECT r.SurveyId, d.RegionLocation, s.StationNum, d.RegionZone, d.RegionTitle, d.UnidentifiedBasketCount, d.Completeness, s.TimeUTC "
        + "FROM basket_region_surveys r JOIN basket_region_survey_details d ON d.Counter = r.Counter "
        + "JOIN stations s ON s.Counter = r.Counter WHERE r.Counter = $counter";
      cmd.Parameters.Add("counter", SqliteType.Integer).Value = counter;
      using var reader = cmd.ExecuteReader();
      if (!reader.Read())
        return null;
      var surveyId = Guid.Parse(reader.GetString(0));
      var region = new BasketPosition
      {
        Location = (BasketLocationEnum)reader.GetInt32(1),
        LocationNum = reader.GetInt32(2),
        Zone = reader.IsDBNull(3) ? null : reader.GetInt32(3),
        LocationTitle = reader.IsDBNull(4) ? null : reader.GetString(4),
      };
      var unidentifiedBasketCount = reader.GetInt32(5);
      var completeness = (BasketRegionSurveyCompleteness)reader.GetInt32(6);
      var timeUTC = new DateTime(reader.GetInt64(7), DateTimeKind.Utc);
      reader.Close();

      cmd.CommandText =
        "SELECT BasketId FROM basket_region_survey_baskets WHERE Counter = $counter ORDER BY BasketId";
      using var basketReader = cmd.ExecuteReader();
      var basketIds = ImmutableSortedSet.CreateBuilder<int>();
      while (basketReader.Read())
        basketIds.Add(basketReader.GetInt32(0));
      basketReader.Close();
      var (source, correlationId) = BasketEvidenceSourceForCounter(counter, trans);
      return new BasketRegionSurvey
      {
        SurveyId = surveyId,
        Region = region,
        ObservedBasketIds = basketIds.ToImmutable(),
        UnidentifiedBasketCount = unidentifiedBasketCount,
        Completeness = completeness,
        Source = source,
        CorrelationId = correlationId,
        TimeUTC = timeUTC,
        EventCounter = counter,
      };
    }

    private static void ValidateBasketRegionSurvey(
      Guid surveyId,
      BasketPosition region,
      ImmutableSortedSet<int> observedBasketIds,
      int unidentifiedBasketCount,
      BasketRegionSurveyCompleteness completeness
    )
    {
      if (surveyId == Guid.Empty)
        throw new ArgumentException("Survey ID can not be empty.", nameof(surveyId));
      ValidateBasketRegion(region);
      ArgumentNullException.ThrowIfNull(observedBasketIds);
      if (observedBasketIds.Any(id => id <= 0))
        throw new ArgumentException(
          "Observed basket IDs must be positive.",
          nameof(observedBasketIds)
        );
      if (unidentifiedBasketCount < 0)
        throw new ArgumentOutOfRangeException(nameof(unidentifiedBasketCount));
      if (
        region.Location == BasketLocationEnum.LoadUnload
        && (
          observedBasketIds.Count > 1
          || unidentifiedBasketCount > 1
          || observedBasketIds.Count == 1 && unidentifiedBasketCount > 0
        )
      )
        throw new ArgumentException(
          "A load station can contain at most one basket.",
          nameof(observedBasketIds)
        );
      if (!Enum.IsDefined(completeness))
        throw new ArgumentOutOfRangeException(nameof(completeness));
    }

    private static void ValidateBasketRegion(BasketPosition region)
    {
      ArgumentNullException.ThrowIfNull(region);
      if (region.LocationNum <= 0 || region.Zone is <= 0)
        throw new ArgumentException("Basket region numbers must be positive.", nameof(region));
    }

    private static string BasketRegionSurveyFingerprint(
      BasketPosition region,
      ImmutableSortedSet<int> observedBasketIds,
      int unidentifiedBasketCount,
      BasketRegionSurveyCompleteness completeness,
      BasketEvidenceSource source
    )
    {
      var fingerprint = new StringBuilder();
      AppendBasketPositionFingerprint(fingerprint, region);
      foreach (var basketId in observedBasketIds)
        AppendFingerprint(fingerprint, basketId.ToString(CultureInfo.InvariantCulture));
      AppendFingerprint(
        fingerprint,
        unidentifiedBasketCount.ToString(CultureInfo.InvariantCulture)
      );
      AppendFingerprint(fingerprint, completeness.ToString());
      AppendBasketEvidenceSourceFingerprint(fingerprint, source);
      return fingerprint.ToString();
    }
  }
}
