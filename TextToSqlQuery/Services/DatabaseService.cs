using Dapper;
using System.Data.SqlClient;
using TextToSqlQuery.Models.Database;

namespace TextToSqlQuery.Services
{
    public class DatabaseService
    {
        public async Task<DatabaseSchema> GetDatabaseSchemaAsync(string connectionString)
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            // after filling the tableInfo the tables get add in databaseSchema
            var schema = new DatabaseSchema();

            // Get all tables
            var tables = await connection.QueryAsync<string>(@"
            SELECT TABLE_NAME 
            FROM INFORMATION_SCHEMA.TABLES 
            WHERE TABLE_TYPE = 'BASE TABLE'
            ORDER BY TABLE_NAME");

            foreach (var tableName in tables)
            {
                // table info model save the table name and that table col
                var tableInfo = new TableInfo 
                { TableName = tableName };

                // Get columns for each table
                var columns = await connection.QueryAsync<dynamic>(@"
                SELECT 
                    c.COLUMN_NAME,
                    c.DATA_TYPE,
                    c.IS_NULLABLE,
                    CASE WHEN pk.COLUMN_NAME IS NOT NULL THEN 1 ELSE 0 END AS IsPrimaryKey
                FROM INFORMATION_SCHEMA.COLUMNS c
                LEFT JOIN (
                    SELECT ku.TABLE_NAME, ku.COLUMN_NAME
                    FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                    JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE ku
                        ON tc.CONSTRAINT_NAME = ku.CONSTRAINT_NAME
                    WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
                ) pk ON c.TABLE_NAME = pk.TABLE_NAME AND c.COLUMN_NAME = pk.COLUMN_NAME
                WHERE c.TABLE_NAME = @TableName
                ORDER BY c.ORDINAL_POSITION",
                    new { TableName = tableName });

                foreach (var col in columns)
                {
                    tableInfo.Columns.Add(new ColumnInfo
                    {
                        ColumnName = col.COLUMN_NAME,
                        DataType = col.DATA_TYPE,
                        IsNullable = col.IS_NULLABLE == "YES",
                        IsPrimaryKey = col.IsPrimaryKey == 1
                    });
                }

                schema.Tables.Add(tableInfo);
            }

            return schema;
        }

        public async Task<List<Dictionary<string, object>>> ExecuteQueryAsync(
            string connectionString,
            string query)
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            var result = await connection.QueryAsync(query);
            var data = new List<Dictionary<string, object>>();

            foreach (var row in result)
            {
                var dict = new Dictionary<string, object>();
                var rowDict = (IDictionary<string, object>)row;

                foreach (var kvp in rowDict)
                {
                    dict[kvp.Key] = kvp.Value ?? DBNull.Value;
                }

                data.Add(dict);
            }

            return data;
        }

        public async Task<bool> TestConnectionAsync(string connectionString)
        {
            try
            {
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
