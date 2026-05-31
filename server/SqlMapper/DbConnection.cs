using System;
using System.Data;
using Npgsql;

public static class DbConnection
{
    private const string DefaultSystemConnection =
        "Host=localhost;Port=5432;Database=finance_tracker;Username=postgres;Password=password";

    private const string DefaultUsersConnection =
        "Host=localhost;Port=5432;Database=finance_tracker;Username=app_user;Password=password";

    private static string SystemConnectionString;
    private static string UsersConnectionString;

    static DbConnection()
    {
        SystemConnectionString = Environment.GetEnvironmentVariable("SYSTEM_DB_CONNECTION")
                                 ?? DefaultSystemConnection;

        UsersConnectionString = Environment.GetEnvironmentVariable("USERS_DB_CONNECTION")
                                ?? DefaultUsersConnection;
    }

    public static void Configure(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        SystemConnectionString = connectionString;
        UsersConnectionString = connectionString;
    }

    public static IDbConnection SystemConnection
        => new NpgsqlConnection(SystemConnectionString);

    public static IDbConnection UsersConnection
    {
        get
        {
            var csb = new NpgsqlConnectionStringBuilder(UsersConnectionString)
            {
                ApplicationName = "FinanceTracker.Api",
                Timezone = "UTC"
            };

            return new NpgsqlConnection(csb.ConnectionString);
        }
    }
}
