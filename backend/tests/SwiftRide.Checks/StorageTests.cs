using System.Diagnostics;
using Xunit;

public sealed class StorageTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task DatabaseOwnershipAndConstraintsAreEnforced()
    {
        await Psql("trip", "PayDB", "SELECT 1;", succeeds: false);
        await Psql("payment_app", "TripDB", "SELECT 1;", succeeds: false);
        var roles = await Psql(
            "swiftride_admin",
            "postgres",
            "SELECT COUNT(*) FROM pg_roles WHERE rolname IN ('trip','payment_app') "
                + "AND NOT rolsuper AND NOT rolcreatedb AND NOT rolcreaterole;"
        );
        Assert.Equal("2", roles.Trim());
        var tables = await Psql(
            "trip",
            "TripDB",
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='public' "
                + "AND table_name IN ('Trips','InboxState','OutboxState','OutboxMessage');"
        );
        Assert.Equal("4", tables.Trim());
        await Psql(
            "payment_app",
            "PayDB",
            """BEGIN; UPDATE "WalletAccounts" SET "Available" = -1; ROLLBACK;""",
            succeeds: false
        );
    }

    private static async Task<string> Psql(
        string user,
        string database,
        string sql,
        bool succeeds = true
    )
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (
            directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "docker-compose.yml"))
        )
            directory = directory.Parent;
        Assert.NotNull(directory);
        var start = new ProcessStartInfo("docker")
        {
            WorkingDirectory = directory.FullName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (
            var arg in new[]
            {
                "compose",
                "exec",
                "-T",
                "postgres",
                "psql",
                "-X",
                "-U",
                user,
                "-d",
                database,
                "-v",
                "ON_ERROR_STOP=1",
                "-tAc",
                sql,
            }
        )
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
        Assert.True(succeeds == (process.ExitCode == 0), await error);
        return await output;
    }
}
