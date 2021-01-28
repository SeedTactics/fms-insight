/* Copyright (c) 2021, John Lenz

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
using System.Linq;
using System.Data;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace BlackMaple.MachineFramework
{
  public partial class Repository : IDisposable, IRepository
  {
    private SqliteConnection _connection;
    private bool _closeConnectionOnDispose;
    private RepositoryConfig _cfg;

    internal Repository(RepositoryConfig cfg, SqliteConnection c, bool closeOnDispose)
    {
      _connection = c;
      _closeConnectionOnDispose = closeOnDispose;
      _cfg = cfg;
    }

    public void Close()
    {
      _connection.Close();
    }

    public void Dispose()
    {
      if (_closeConnectionOnDispose && _connection != null)
      {
        _connection.Close();
        _connection.Dispose();
        _connection = null;
      }
    }
  }

  public class RepositoryConfig
  {
    public event Action<MachineWatchInterface.LogEntry, string, IRepository> NewLogEntry;
    internal void OnNewLogEntry(MachineWatchInterface.LogEntry e, string foreignId, IRepository db) => NewLogEntry?.Invoke(e, foreignId, db);

    public FMSSettings Settings { get; }

    public static RepositoryConfig InitializeEventDatabase(FMSSettings st, string filename, string oldInspDbFile = null, string oldJobDbFile = null)
    {
      var connStr = "Data Source=" + filename;
      if (System.IO.File.Exists(filename))
      {
        using (var conn = new SqliteConnection(connStr))
        {
          conn.Open();
          DatabaseSchema.UpgradeTables(conn, st, oldInspDbFile, oldJobDbFile);
          return new RepositoryConfig(st, connStr);
        }
      }
      else
      {
        using (var conn = new SqliteConnection(connStr))
        {
          conn.Open();
          try
          {
            DatabaseSchema.CreateTables(conn, st);
            return new RepositoryConfig(st, connStr);
          }
          catch
          {
            conn.Close();
            System.IO.File.Delete(filename);
            throw;
          }
        }
      }
    }

    public static RepositoryConfig InitializeSingleThreadedMemoryDB(FMSSettings st)
    {
      var memConn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
      memConn.Open();
      DatabaseSchema.CreateTables(memConn, st);
      return new RepositoryConfig(st, memConn);
    }

    public static RepositoryConfig InitializeSingleThreadedMemoryDB(FMSSettings st, SqliteConnection memConn, bool createTables)
    {
      if (createTables)
      {
        DatabaseSchema.CreateTables(memConn, st);
      }
      else
      {
        DatabaseSchema.UpgradeTables(memConn, st, null, null);
      }
      return new RepositoryConfig(st, memConn);
    }

    public IRepository OpenConnection()
    {
      if (_memoryConnection != null)
      {
        return new Repository(this, _memoryConnection, closeOnDispose: false);
      }
      else
      {
        var conn = new SqliteConnection(_connStr);
        conn.Open();
        return new Repository(this, conn, closeOnDispose: true);
      }
    }

    public void CloseMemoryConnection()
    {
      _memoryConnection?.Close();
    }

    private string _connStr { get; }
    private SqliteConnection _memoryConnection { get; }

    private RepositoryConfig(FMSSettings st, string connStr)
    {
      Settings = st;
      _connStr = connStr;
    }

    private RepositoryConfig(FMSSettings st, SqliteConnection memConn)
    {
      Settings = st;
      _memoryConnection = memConn;
    }
  }
}