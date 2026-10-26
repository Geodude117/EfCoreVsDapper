using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace EfCoreVsDapper.Api.Data.DapperSql;

/// <summary>
/// Hands Dapper a new, unopened connection for each operation.
/// ADO.NET pools the underlying connections, so this is cheap.
/// </summary>
public class SqlConnectionFactory(string connectionString)
{
    public DbConnection Create() => new SqlConnection(connectionString);
}
