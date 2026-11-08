using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Speranta.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSperantaPersistence(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres");

        // Empty, not just missing: appsettings.json ships "" as a placeholder, and
        // an empty string sails past a null check only to fail much later inside
        // Npgsql with "The ConnectionString property has not been initialized."
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:Postgres is not configured. See README.md.");
        }

        services.AddDbContext<SperantaDbContext>(options => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention());

        return services;
    }
}
