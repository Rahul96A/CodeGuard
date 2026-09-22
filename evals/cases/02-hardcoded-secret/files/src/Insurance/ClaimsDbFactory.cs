using Microsoft.Data.SqlClient;

namespace Insurance;

public static class ClaimsDbFactory
{
    private const string ConnectionString =
        "Server=tcp:claims-prod.database.windows.net;Database=Claims;User ID=claims_admin;Password=Winter2026!Sydney;";

    public static SqlConnection Create() => new(ConnectionString);
}
