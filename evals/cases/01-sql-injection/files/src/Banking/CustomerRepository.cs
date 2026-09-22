using Microsoft.Data.SqlClient;

namespace Banking;

public sealed class CustomerRepository(string connectionString)
{
    public async Task<string?> FindCustomerNameAsync(string surname)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        var sql = $"SELECT TOP 1 FullName FROM Customers WHERE Surname = '{surname}'";
        await using var cmd = new SqlCommand(sql, conn);
        return (string?)await cmd.ExecuteScalarAsync();
    }
}
