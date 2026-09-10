using LedSupBot.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace LedSupBot.Infrastructure.Persistence;

/// <summary>
/// Used by `dotnet ef` only. Does not run migrations at application startup.
/// </summary>
public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var apiPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "LedSupBot.Api"));
        if (!Directory.Exists(apiPath))
        {
            apiPath = Directory.GetCurrentDirectory();
        }

        var configuration = new ConfigurationBuilder()
            .SetBasePath(apiPath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .AddFlatEnvironmentVariableBridge()
            .Build();

        var connectionString = configuration.GetSection(DatabaseOptions.SectionName)[nameof(DatabaseOptions.ConnectionString)];
        if (string.IsNullOrWhiteSpace(connectionString)) connectionString = Environment.GetEnvironmentVariable("DATABASE_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString)) connectionString = "Host=localhost;Port=5432;Database=led_sup_bot;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                npgsql.UseVector();
            })
            .Options;

        return new ApplicationDbContext(options);
    }
}
