using Dapper;
using Microsoft.Data.SqlClient;

namespace Banking;

public sealed class AccountQueries(string connectionString)
{
    public async Task<decimal> GetBalanceAsync(Guid accountId, CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        return await conn.ExecuteScalarAsync<decimal>(
            new CommandDefinition("SELECT Balance FROM Accounts WHERE Id = @accountId", new { accountId }, cancellationToken: ct));
    }
}
