using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace LifeLedger.Api.Tests;

/// <summary>Hosts the API for integration tests with an isolated, unseeded SQLite database.</summary>
public sealed class LifeLedgerApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Unique temporary database path used only by this test host.</summary>
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"lifeledger-tests-{Guid.NewGuid():N}.db");
    /// <summary>Temporary directory for local marker files created by destructive demo and delete endpoints.</summary>
    private readonly string _demoDataDirectory = Path.Combine(Path.GetTempPath(), $"lifeledger-demo-tests-{Guid.NewGuid():N}");

    /// <summary>Overrides runtime configuration so tests never open the user's local database.</summary>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["ConnectionStrings:LifeLedger"] = $"Data Source={_databasePath}",
            ["SeedDemoData"] = "false",
            ["DemoDataDirectory"] = _demoDataDirectory
        }));
    }

    /// <summary>Disposes the host and removes SQLite data files created for the test run.</summary>
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            DeleteIfExists(_databasePath);
            DeleteIfExists($"{_databasePath}-shm");
            DeleteIfExists($"{_databasePath}-wal");
            if (Directory.Exists(_demoDataDirectory)) Directory.Delete(_demoDataDirectory, recursive: true);
        }
    }

    /// <summary>Removes a temporary SQLite file when it exists.</summary>
    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
