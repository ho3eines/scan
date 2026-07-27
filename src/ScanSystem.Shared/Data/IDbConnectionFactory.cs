using Microsoft.Data.SqlClient;

namespace ScanSystem.Shared.Data;

/// <summary>
/// ساخت SqlConnection بر اساس Connection String.
/// Connection String در زمان Build اول توسط Web/App تزریق می‌شود.
/// </summary>
public interface IDbConnectionFactory
{
    SqlConnection CreateConnection();
}

public class SqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public SqlConnectionFactory(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Connection String تنظیم نشده است.");
        _connectionString = connectionString;
    }

    public SqlConnection CreateConnection()
    {
        var conn = new SqlConnection(_connectionString);
        return conn;
    }
}
