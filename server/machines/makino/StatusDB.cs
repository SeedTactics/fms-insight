/* Copyright (c) 2018, John Lenz

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
using System.Data;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace Makino
{
	public class StatusDB
	{
		#region Constructor and Init
		private SqliteConnection _connection;
		private object _lock;

		public StatusDB(string filename)
		{
			_lock = new object();
			if (System.IO.File.Exists(filename)) {
        _connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + filename);
				_connection.Open();
				UpdateTables();
			}
			else {
        _connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + filename);
				_connection.Open();
				try {
					CreateTables();
				}
				catch {
					_connection.Close();
					System.IO.File.Delete(filename);
					throw;
				}
			}
		}

		public StatusDB(SqliteConnection conn)
		{
			_lock = new object();
			_connection = conn;
		}

		public void Close()
		{
			_connection.Close();
		}
		#endregion

		#region Create/Update
		private const int Version = 1;

		public void CreateTables()
		{
			using (var cmd = _connection.CreateCommand()) {

			cmd.CommandText = "CREATE TABLE version(ver INTEGER)";
			cmd.ExecuteNonQuery();
			cmd.CommandText = "INSERT INTO version VALUES(" + Version.ToString() + ")";
			cmd.ExecuteNonQuery();

			cmd.CommandText = "CREATE TABLE matids(Pallet INTEGER, FixtureNum INTEGER, LoadedUTC INTEGER, LocCounter INTEGER, OrderName TEXT, MaterialID INTEGER, PRIMARY KEY(Pallet, FixtureNum, LoadedUTC, LocCounter))";
			cmd.ExecuteNonQuery();
			cmd.CommandText = "CREATE INDEX matids_idx ON matids(MaterialID)";
			cmd.ExecuteNonQuery();
			}
		}

		private void UpdateTables()
		{
			using (var cmd = _connection.CreateCommand()) {

			cmd.CommandText = "SELECT ver FROM version";

			int curVersion = 0;

			using (var reader = cmd.ExecuteReader()) {
				if (reader.Read())
					curVersion = (int)reader.GetInt32(0);
				else
					curVersion = 0;
			}

			if (curVersion > Version)
				throw new ApplicationException("This input file was created with a newer version of Machine Watch.  Please upgrade Machine Watch");

			if (curVersion == Version) return;


			var trans = _connection.BeginTransaction();

			try {
				//add upgrade code here, in seperate functions
				//if (curVersion < 1) Ver0ToVer1(trans);

				//update the version in the database
				cmd.Transaction = trans;
				cmd.CommandText = "UPDATE version SET ver = " + Version.ToString();
				cmd.ExecuteNonQuery();

				trans.Commit();
			} catch {
				trans.Rollback();
				throw;
			}

			//only vacuum if we did some updating
			cmd.Transaction = null;
			cmd.CommandText = "VACUUM";
			cmd.ExecuteNonQuery();
			}
		}

		public void SetFirstMaterialID(long matID)
		{
			using (var cmd = _connection.CreateCommand()) {
				cmd.CommandText = "INSERT INTO matids(Pallet, FixtureNum, LoadedUTC, LocCounter, OrderName, MaterialID)" +
					" VALUES(-1, -1, ?, -1, '', ?)";
				cmd.Parameters.Add("", SqliteType.Integer).Value = DateTime.MinValue.Ticks;
				cmd.Parameters.Add("", SqliteType.Integer).Value = matID;
				cmd.ExecuteNonQuery();
			}
		}
        #endregion

		#region MatIDs
		public struct MatIDRow
		{
			public DateTime LoadedUTC;
			public int LocCounter;
			public string Order;
			public long MatID;
		}

		public IList<MatIDRow> FindMaterialIDs(int pallet, int fixturenum, DateTime loadedUTC)
		{
			lock (_lock) {
				var trans = _connection.BeginTransaction();
				try {

					var ret = LoadMatIDs(pallet, fixturenum, loadedUTC, trans);

					trans.Commit();

					return ret;
				} catch {
					trans.Rollback();
					throw;
				}
			}
		}

		public IList<MatIDRow> CreateMaterialIDs(int pallet, int fixturenum, DateTime loadedUTC,
			string order, IReadOnlyList<long> materialIds, int startingCounter)
		{
			lock (_lock) {
				var trans = _connection.BeginTransaction();
				try {

					var ret = AddMatIDs(pallet, fixturenum, loadedUTC, order, materialIds, startingCounter, trans);

					trans.Commit();
					return ret;
				} catch {
					trans.Rollback();
					throw;
				}
			}
		}

		private List<MatIDRow> LoadMatIDs(int pallet, int fixturenum, DateTime beforeLoadedUTC, IDbTransaction trans)
		{
			var ret = new List<MatIDRow>();

			using (var cmd = _connection.CreateCommand()) {
				((IDbCommand)cmd).Transaction = trans;

				cmd.CommandText = "SELECT LoadedUTC, LocCounter, OrderName, MaterialID FROM " +
					"matids WHERE Pallet = ? AND FixtureNum = ? AND LoadedUTC <= ? " +
					"ORDER BY LoadedUTC DESC";
				cmd.Parameters.Add("", SqliteType.Integer).Value = pallet;
				cmd.Parameters.Add("", SqliteType.Integer).Value = fixturenum;
				cmd.Parameters.Add("", SqliteType.Integer).Value = beforeLoadedUTC.Ticks;

				DateTime lastTime = DateTime.MaxValue;

				using (IDataReader reader = cmd.ExecuteReader()) {
					while (reader.Read()) {

						//Only read a single LoadedUTC time
						var loadedUTC = new DateTime(reader.GetInt64(0), DateTimeKind.Utc);
						if (lastTime == DateTime.MaxValue)
							lastTime = loadedUTC;
						else if (lastTime != loadedUTC)
							break;

						var row = default(MatIDRow);
						row.LoadedUTC = loadedUTC;
						row.LocCounter = reader.GetInt32(1);
						row.Order = reader.GetString(2);
						row.MatID = reader.GetInt64(3);
						ret.Add(row);
					}
				}
			}

			return ret;
		}

		private List<MatIDRow> AddMatIDs(int pallet, int fixturenum, DateTime loadedUTC, string order,
			IReadOnlyList<long> materialIds, int counterStart, IDbTransaction trans)
		{
			var ret = new List<MatIDRow>();

			using (var cmd = _connection.CreateCommand()) {
				((IDbCommand)cmd).Transaction = trans;

				cmd.CommandText = "INSERT INTO matids(Pallet, FixtureNum, LoadedUTC, LocCounter, OrderName, MaterialID)" +
					" VALUES (?,?,?,?,?,?)";
				cmd.Parameters.Add("", SqliteType.Integer).Value = pallet;
				cmd.Parameters.Add("", SqliteType.Integer).Value = fixturenum;
				cmd.Parameters.Add("", SqliteType.Integer).Value = loadedUTC.Ticks;
				cmd.Parameters.Add("", SqliteType.Integer);
				cmd.Parameters.Add("", SqliteType.Text).Value = order;
				cmd.Parameters.Add("", SqliteType.Integer);

				for (int i = 0; i < materialIds.Count; i++) {
					cmd.Parameters[3].Value = counterStart + i;
					cmd.Parameters[5].Value = materialIds[i];
					cmd.ExecuteNonQuery();

					var row = default(MatIDRow);
					row.LoadedUTC = loadedUTC;
					row.LocCounter = counterStart + i;
					row.Order = order;
					row.MatID = materialIds[i];
					ret.Add(row);
				}
			}

			return ret;
		}
        #endregion
	}
}

