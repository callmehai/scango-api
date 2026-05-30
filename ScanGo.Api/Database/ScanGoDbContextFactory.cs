using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ScanGo.Api.Database;

/// <summary>
/// Used only by EF Core CLI (`dotnet ef migrations add`, `dotnet ef database update`).
/// Reads from env var SCANGO_DESIGN_CONN with a localhost fallback.
/// </summary>
public class ScanGoDbContextFactory : IDesignTimeDbContextFactory<ScanGoDbContext>
{
    public ScanGoDbContext CreateDbContext(string[] args)
    {
        var conn =
            Environment.GetEnvironmentVariable("SCANGO_DESIGN_CONN")
            ?? "Host=localhost;Port=5433;Database=scango;Username=scango;Password=scango";

        var opts = new DbContextOptionsBuilder<ScanGoDbContext>()
            .UseNpgsql(conn)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ScanGoDbContext(opts);
    }
}
