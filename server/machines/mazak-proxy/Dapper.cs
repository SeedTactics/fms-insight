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
using System.Data;
using System.Linq;

// A few methods with the same API as dapper but directly implemented for .NET 3.5

public static class DapperQueryExecute
{
  public static IEnumerable<T> Query<T>(this IDbConnection conn, string sql, IDbTransaction transaction)
  {
    // implement Query just like dapper, but as a fallback for when dapper is not available
    var result = new List<T>();
    using var cmd = conn.CreateCommand();
    cmd.Transaction = transaction;
    cmd.CommandText = sql;
    using var reader = cmd.ExecuteReader();

    var props = typeof(T).GetProperties().ToDictionary(p => p.Name);

    while (reader.Read())
    {
      var obj = Activator.CreateInstance<T>();
      for (int i = 0; i < reader.FieldCount; i++)
      {
        var name = reader.GetName(i);
        if (props.TryGetValue(name, out var prop))
        {
          var val = reader[i];
          if (val != DBNull.Value && val != null)
          {
            Type t = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            if (t.IsEnum)
            {
              prop.SetValue(obj, Enum.ToObject(t, val), null);
            }
            else
            {
              prop.SetValue(obj, Convert.ChangeType(val, t), null);
            }
          }
        }
      }
      result.Add(obj);
    }

    return result;
  }

  private class PropAndParam
  {
    public System.Reflection.PropertyInfo Prop { get; set; }
    public IDbDataParameter Param { get; set; }
  }

  public static void Execute<T>(
    this IDbConnection conn,
    string sql,
    IEnumerable<T> objs,
    IDbTransaction transaction
  )
  {
    using var cmd = conn.CreateCommand();
    cmd.Transaction = transaction;
    cmd.CommandText = sql;

    var propInfo = typeof(T).GetProperties().ToDictionary(p => p.Name.ToLower());
    var paramRegex = new System.Text.RegularExpressions.Regex(@"@(\w+)");

    var props = new List<PropAndParam>();

    foreach (var param in paramRegex.Matches(sql).Cast<System.Text.RegularExpressions.Match>())
    {
      var paramName = param.Groups[1].Value;
      if (propInfo.TryGetValue(paramName.ToLower(), out var prop))
      {
        var paramDb = cmd.CreateParameter();
        paramDb.ParameterName = "@" + paramName;
        cmd.Parameters.Add(paramDb);
        props.Add(new PropAndParam() { Prop = prop, Param = paramDb });
      }
    }

    foreach (var obj in objs)
    {
      foreach (var pp in props)
      {
        var val = pp.Prop.GetValue(obj, null);
        if (val == null)
        {
          pp.Param.Value = DBNull.Value;
        }
        else
        {
          pp.Param.Value = val;
        }
      }
      cmd.ExecuteNonQuery();
    }
  }
}
