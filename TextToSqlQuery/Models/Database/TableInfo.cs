namespace TextToSqlQuery.Models.Database
{
    public class TableInfo
    {
        public string TableName { get; set; } = string.Empty;
        public List<ColumnInfo> Columns { get; set; } = new();

    }
}
