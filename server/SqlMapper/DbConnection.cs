using System;
using System.Data;
using Npgsql;

public static class DbConnection
{
    private static readonly string DefaultSystemConnection =
        "Host=localhost;Port=5432;Database=Pipeline;Username=postgres;Password=password";

    private static readonly string DefaultUsersConnection =
        "Host=localhost;Port=5432;Database=Pipeline;Username=app_user;Password=password";

    // These will get initialized once when the class loads.
    private static readonly string SystemConnectionString;
    private static readonly string UsersConnectionString;

    static DbConnection()
    {
        SystemConnectionString = Environment.GetEnvironmentVariable("SYSTEM_DB_CONNECTION")
                                 ?? DefaultSystemConnection;

        UsersConnectionString = Environment.GetEnvironmentVariable("USERS_DB_CONNECTION")
                                ?? DefaultUsersConnection;
    }

    public static IDbConnection SystemConnection
        => new NpgsqlConnection(SystemConnectionString);

    public static IDbConnection UsersConnection
    {
        get
        {
            var csb = new NpgsqlConnectionStringBuilder(UsersConnectionString)
            {
                ApplicationName = "MyApp",
                Timezone = "UTC"
            };

            return new NpgsqlConnection(csb.ConnectionString);
        }
    }
}
