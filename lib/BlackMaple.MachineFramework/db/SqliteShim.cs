#if SYSTEM_DATA_SQLITE
using System.Data;
using SQ=System.Data.SQLite;

namespace BlackMaple.MachineFramework
{
    public enum SqliteType
    {
        Text,
        Integer,
        Real
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
