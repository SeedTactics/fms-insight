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
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

#nullable enable

namespace BlackMaple.MachineFramework
{
  internal sealed partial class Repository : IDisposable, IRepository
  {
    private readonly RepositoryConfig _cfg;
    public RepositoryConfig RepoConfig => _cfg;
    private SqliteConnection? _connection;

    internal Repository(RepositoryConfig cfg, SqliteConnection c)
    {
      _connection = c;
      _cfg = cfg;
    }

    void IDisposable.Dispose()
    {
      if (_connection != null)
      {
        _connection.Close();
        _connection.Dispose();
        _connection = null;
      }
    }
  }

  public sealed class RepositoryConfig : IDisposable
  {
    public event Action<LogEntry, string, IRepository>? NewLogEntry;
    public SerialSettings? SerialSettings { get; }
    private readonly string _connStr;
    private SqliteConnection? _memoryConnection = null;

    internal void OnNewLogEntry(LogEntry e, string foreignId, IRepository db) =>
      NewLogEntry?.Invoke(e, foreignId, db);

    public static RepositoryConfig InitializeEventDatabase(
      SerialSettings? st,
      string filename,
      string? oldInspDbFile = null,
      string? oldJobDbFile = null
    )
    {
      var connStr = "Data Source=" + filename;
      if (System.IO.File.Exists(filename))
      {
        using var conn = new SqliteConnection(connStr);
        conn.Open();
        DatabaseSchema.UpgradeTables(conn, st, oldInspDbFile, oldJobDbFile);
        return new RepositoryConfig(st, connStr, null);
      }
      else
      {
        using var conn = new SqliteConnection(connStr);
        conn.Open();
        try
        {
          DatabaseSchema.CreateTables(conn, st);
          return new RepositoryConfig(st, connStr, null);
        }
        catch
        {
          conn.Close();
          System.IO.File.Delete(filename);
          throw;
        }
      }
    }

    public static RepositoryConfig InitializeMemoryDB(
      SerialSettings? st,
      Guid? guid = null,
      bool createTables = true
    )
    {
      var guidStr = (guid ?? Guid.NewGuid()).ToString();
      var connStr = $"Data Source=file:${guidStr}?mode=memory&cache=shared";
      // need to keep a memory connection open, since sqlite reclaims the memory once the last
      // connection closes.  This connection is kept private and new connections using the same guid
      // are openend for all operations.
      var conn = new SqliteConnection(connStr);
      conn.Open();
      if (createTables)
      {
        DatabaseSchema.CreateTables(conn, st);
      }
      return new RepositoryConfig(st, connStr, conn);
    }

    public IRepository OpenConnection()
    {
      var conn = new SqliteConnection(_connStr);
      conn.Open();
      return new Repository(this, conn);
    }

    public void Dispose()
    {
      if (_memoryConnection != null)
      {
        _memoryConnection.Close();
        _memoryConnection = null;
      }
    }

    private RepositoryConfig(SerialSettings? st, string connStr, SqliteConnection? memConn)
    {
      SerialSettings = st;
      _connStr = connStr;
      _memoryConnection = memConn;
    }
  }

  public static class RepositoryService
  {
    public static IServiceCollection AddRepository(
      this IServiceCollection s,
      string filename,
      SerialSettings serial,
      string? oldInspDbFile = null,
      string? oldJobDbFile = null
    )
    {
      return s.AddSingleton<RepositoryConfig>(
        (_) => RepositoryConfig.InitializeEventDatabase(serial, filename, oldInspDbFile, oldJobDbFile)
      );
    }

    public static IServiceCollection AddMemoryRepository(
      this IServiceCollection s,
      SerialSettings serial,
      Guid? guid = null,
      bool createTables = true
    )
    {
      return s.AddSingleton<RepositoryConfig>(
        (_) => RepositoryConfig.InitializeMemoryDB(serial, guid, createTables)
      );
    }
  }
}
