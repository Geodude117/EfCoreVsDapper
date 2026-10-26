using Microsoft.Data.SqlClient;

namespace EfCoreVsDapper.Api.Data;

internal static class SqlErrors
{
    // 2627: unique constraint violation, 2601: duplicate key in a unique index
    private static readonly int[] UniqueViolationNumbers = [2601, 2627];

    /// <summary>
    /// True if the exception, or any exception it wraps, is a SQL Server unique-key violation.
    /// EF Core wraps the SqlException in a DbUpdateException; Dapper throws it directly.
    /// </summary>
    public static bool IsUniqueViolation(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
        {
            if (ex is SqlException sql && UniqueViolationNumbers.Contains(sql.Number))
                return true;
        }
        return false;
    }
}
