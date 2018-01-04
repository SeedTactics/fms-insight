/* Copyright (c) 2017, John Lenz

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

#if SYSTEM_DATA_SQLITE
using System.Data;
using SQ=System.Data.SQLite;

namespace BlackMaple.MachineFramework
{
    public enum SqliteType
    {
        Text,
        Integer,
        Real,
        Blob
    }

    public static class SqliteExtensions
    {
        public static SQ.SQLiteParameter Add(this SQ.SQLiteParameterCollection p, string paramVal, SqliteType t)
        {
            switch (t) {
                case SqliteType.Text:
                    return p.Add(paramVal, DbType.String);
                case SqliteType.Integer:
                    return p.Add(paramVal, DbType.Int64);
                case SqliteType.Real:
                    return p.Add(paramVal, DbType.Double);
                case SqliteType.Blob:
                    return p.Add(paramVal, DbType.Binary);
            }
            return null;
        }

        private const string ConnectionString = "Compress=False;Synchronous=Full;Version=3;DateTimeFormat=Ticks;";

        public static SqliteConnection Connect(string filename, bool newFile)
        {
            var connS = ConnectionString;
            if (newFile) {
                connS += "New=True;";
            }
            connS += "Data Source=" + filename;
            return new SqliteConnection(new SQ.SQLiteConnection(connS));
        }

	public static SqliteConnection ConnectMemory()
	{
            return new SqliteConnection(new SQ.SQLiteConnection("Data Source=:memory:"));
	}
    }

    public class SqliteConnection
    {
        private SQ.SQLiteConnection _conn;

        internal SqliteConnection(SQ.SQLiteConnection s)
        {
            _conn = s;
        }

        public void Open()
        {
            _conn.Open();
        }

        public void Close()
        {
            _conn.Close();
        }

        public SQ.SQLiteTransaction BeginTransaction()
        {
            return _conn.BeginTransaction();
        }

        public SQ.SQLiteCommand CreateCommand()
        {
            return _conn.CreateCommand();
        }
    }
}
#else
using Microsoft.Data.Sqlite;

namespace BlackMaple.MachineFramework
{
    public static class SqliteExtensions
    {
        public static SqliteConnection Connect(string filename, bool newFile)
        {
            return new SqliteConnection("Data Source=" + filename);
        }

	public static SqliteConnection ConnectMemory()
	{
            return new SqliteConnection("Data Source=:memory:");
	}
    }
}
#endif
