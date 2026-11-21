using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

static class DatabaseCheck
{
    public static async Task RunAsync()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "appsettings.json"))) directory = directory.Parent;
        if (directory is null) throw new InvalidOperationException("Run from the AiEmailer checkout.");
        var config = new ConfigurationBuilder().SetBasePath(directory.FullName).AddJsonFile("appsettings.json").AddEnvironmentVariables().Build();
        var builder = new SqlConnectionStringBuilder(config.GetConnectionString("Default")) { ConnectTimeout = 5 };
        Console.WriteLine($"Database target: {builder.DataSource} / {builder.InitialCatalog}");
        try
        {
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand("SELECT CASE WHEN OBJECT_ID('dbo.skey_mailbox_connection') IS NULL THEN 0 ELSE 1 END", connection);
            Console.WriteLine("Database reachable; mailbox migration present: " + await command.ExecuteScalarAsync());
        }
        catch (Exception ex) { Console.WriteLine("Database connectivity check blocked/failed: " + ex.GetType().Name); Environment.ExitCode = 2; }
    }
}
