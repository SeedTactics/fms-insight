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
using System.IO;
using System.Linq;

namespace MazakMachineInterface;

public static class LogDataVerE
{
  private const string DateTimeFormat = "yyyyMMddHHmmss";

  public static List<LogEntry> LoadLog(string lastForeignID, IMazakDB _readDB)
  {
    return _readDB.WithReadDBConnection(conn =>
    {
      var trans = conn.BeginTransaction();
      try
      {
        if (
          !System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.Windows
          )
        )
        {
          throw new Exception("VerE only only supported on windows");
        }

        using (System.Data.OleDb.OleDbCommand cmd = (System.Data.OleDb.OleDbCommand)conn.CreateCommand())
        {
          ((System.Data.IDbCommand)cmd).Transaction = trans;

          long epoch = 1;
          long lastID = 0;
          DateTime lastDate = DateTime.MinValue;
          bool useDate = false;
          string[] s = lastForeignID.Split('-');
          if (s.Length == 1)
          {
            epoch = 1;
            if (!long.TryParse(s[0], out lastID))
              useDate = true;
          }
          else if (s.Length == 2)
          {
            if (!long.TryParse(s[0], out epoch))
              useDate = true;
            if (!long.TryParse(s[1], out lastID))
              useDate = true;
          }
          else if (s.Length == 3)
          {
            if (!long.TryParse(s[0], out epoch))
              useDate = true;
            if (!long.TryParse(s[1], out lastID))
              useDate = true;
            lastDate = DateTime.ParseExact(s[2], DateTimeFormat, null);
          }
          else
          {
            useDate = true;
          }

          if (useDate)
          {
            cmd.CommandText =
              "SELECT ID, Date, LogMessageCode, ResourceNumber, PartName, ProcessNumber,"
              + "FixedQuantity, PalletNumber, ProgramNumber, FromPosition, ToPosition "
              + "FROM Log WHERE Date > ? ORDER BY ID ASC";
            var param = cmd.CreateParameter();
            param.OleDbType = System.Data.OleDb.OleDbType.Date;
            param.Value = DateTime.Now.AddDays(-7);
            cmd.Parameters.Add(param);
          }
          else
          {
            CheckIDRollover(trans, conn, ref epoch, ref lastID, lastDate);

            cmd.CommandText =
              "SELECT ID, Date, LogMessageCode, ResourceNumber, PartName, ProcessNumber,"
              + "FixedQuantity, PalletNumber, ProgramNumber, FromPosition, ToPosition "
              + "FROM Log WHERE ID > ? ORDER BY ID ASC";
            var param = cmd.CreateParameter();
            param.OleDbType = System.Data.OleDb.OleDbType.Numeric;
            param.Value = lastID;
            cmd.Parameters.Add(param);
          }

          var ret = new List<LogEntry>();

          using (var reader = cmd.ExecuteReader())
          {
            while (reader.Read())
            {
              if (reader.IsDBNull(0))
                continue;
              if (reader.IsDBNull(1))
                continue;
              if (reader.IsDBNull(2))
                continue;
              if (!Enum.IsDefined(typeof(LogCode), reader.GetInt32(2)))
                continue;

              string fullPartName = reader.IsDBNull(4) ? "" : reader.GetString(4);
              int idx = fullPartName.IndexOf(':');

              var e = new LogEntry()
              {
                ForeignID =
                  epoch.ToString()
                  + "-"
                  + reader.GetInt32(0).ToString()
                  + "-"
                  + reader.GetDateTime(1).ToString(DateTimeFormat),
                TimeUTC = new DateTime(reader.GetDateTime(1).Ticks, DateTimeKind.Local).ToUniversalTime(),
                Code = (LogCode)reader.GetInt32(2),
                StationNumber = reader.IsDBNull(3) ? -1 : reader.GetInt32(3),
                FullPartName = fullPartName,
                JobPartName = (idx > 0) ? fullPartName.Substring(0, idx) : fullPartName,
                Process = reader.IsDBNull(5) ? 1 : reader.GetInt32(5),
                FixedQuantity = reader.IsDBNull(6) ? 1 : reader.GetInt32(6),
                Pallet = reader.IsDBNull(7) ? -1 : reader.GetInt32(7),
                Program = reader.IsDBNull(8) ? "" : reader.GetInt32(8).ToString(),
                FromPosition = reader.IsDBNull(9) ? "" : reader.GetString(9),
                TargetPosition = reader.IsDBNull(10) ? "" : reader.GetString(10),
              };

              ret.Add(e);
            }
          }
          trans.Commit();

          return ret;
        }
      }
      catch
      {
        trans.Rollback();
        throw;
      }
    });
  }

  private static void CheckIDRollover(
    System.Data.IDbTransaction trans,
    System.Data.IDbConnection conn,
    ref long epoch,
    ref long lastID,
    DateTime lastDate
  )
  {
    if (
      !System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
        System.Runtime.InteropServices.OSPlatform.Windows
      )
    )
    {
      throw new Exception("VerE only only supported on windows");
    }

    using (var cmd = conn.CreateCommand())
    {
      cmd.Transaction = trans;
      cmd.CommandText = "SELECT Date FROM Log WHERE ID = ?";
      var param = (System.Data.OleDb.OleDbParameter)cmd.CreateParameter();
      param.OleDbType = System.Data.OleDb.OleDbType.Numeric;
      param.Value = lastID;
      cmd.Parameters.Add(param);

      using (var reader = cmd.ExecuteReader())
      {
        bool foundLine = false;
        while (reader.Read())
        {
          foundLine = true;
          if (lastDate != DateTime.MinValue && reader.GetDateTime(0) != lastDate)
          {
            //roll to new epoch since the date for this ID is different
            epoch += 1;
            lastID = 0;
          }
          break;
        }

        if (!foundLine)
        {
          //roll to new epoch since no ID is found, the data has been deleted
          epoch += 1;
          lastID = 0;
        }
      }
    }
  }
}
