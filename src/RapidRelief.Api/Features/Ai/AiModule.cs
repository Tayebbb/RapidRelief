using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RapidRelief.Api.Features.Ai.Assistant;
using RapidRelief.Api.Features.Ai.Data;
using RapidRelief.Api.Features.Ai.Endpoints;
using RapidRelief.Api.Features.Ai.FreeLlmPool;
using RapidRelief.Api.Features.Ai.Pipeline;
using RapidRelief.Api.Infrastructure.Modules;
using RapidRelief.Shared.Contracts.Eventing;
using RapidRelief.Shared.Contracts.Events;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.Ai;

/// <summary>
/// Tayeb's F8 lane (D-028): the composite FreeLlmPool-with-fallback displaces the direct
/// rule-based binding, while the rule-based service stays registered concretely forever
/// (§4.5/§4.8). Plain Add* — this remains the real-service slot (Order 0).
/// </summary>
public sealed class AiModule : IFeatureModule
{
    public string Name => "Ai";

    public void AddModule(IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        // TryAdd: tests or future composition may pin a fixed TimeProvider first.
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<RuleBasedAiAnalysisService>();
        services.AddSingleton<IAiAnalysisService, FreeLlmPoolAiAnalysisService>();
        services.AddSingleton(sp => new AiCircuitBreaker(
            sp.GetRequiredService<TimeProvider>(),
            config.GetValue("Ai:FreeLlmPool:BreakerFailures", 3),
            TimeSpan.FromMinutes(config.GetValue("Ai:FreeLlmPool:BreakerOpenMinutes", 2.0))));

        // BaseAddress read from config (deployment topology varies), Infinite timeout
        // (linked-CTS per call). Falls back to the local default when blank so a fresh
        // checkout still boots against the docker-compose sidecar.
        // D-117: Render's fromService/hostport resolves to a bare "host:port" with no scheme —
        // auto-prefix "http://" (freellmpool's own container has no TLS) so the Render blueprint
        // needs no manual dashboard step to wire the sidecar in.
        services.AddHttpClient("freellmpool", client =>
        {
            var baseUrl = config["Ai:FreeLlmPool:BaseUrl"];
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                baseUrl = "http://localhost:8080/";
            }
            else if (!baseUrl.Contains("://", StringComparison.Ordinal))
            {
                baseUrl = $"http://{baseUrl}/";
            }
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = Timeout.InfiniteTimeSpan;
        });
        services.AddSingleton<IFreeLlmPoolClient, FreeLlmPoolClient>();

        // Same feature, same external dependency, same shared breaker.
        services.AddSingleton(AssistantOptions.Read(config));
        services.AddSingleton<IAssistantService, FreeLlmPoolAssistantService>();
        services.AddHostedService<AssistantRetentionWorker>();

        services.AddSingleton(sp => AiChannel.Create(
            config.GetValue("Ai:Pipeline:ChannelCapacity", 100),
            sp.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AiChannel).FullName!)));
        services.AddScoped<IEventHandler<IncidentCreated>, IncidentCreatedHandler>();
        services.AddScoped<DuplicateDetector>();
        services.AddHostedService<AiAnalysisWorker>();

        // Npgsql ONLY outside Testing — the test factory injects its own SQLite options.
        if (!env.IsEnvironment("Testing"))
        {
            services.AddDbContext<AiDbContext>(options =>
                options.UseNpgsql(config.GetConnectionString("Postgres"), npgsql =>
                    npgsql.MigrationsHistoryTable(AiDbContext.MigrationsHistoryTableName)));
        }
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        AiEndpoints.Map(endpoints);
        AssistantEndpoints.Map(endpoints);
    }

    public Task MigrateAsync(IServiceProvider scopedServices, CancellationToken ct)
        => scopedServices.GetRequiredService<AiDbContext>().Database.MigrateAsync(ct);
}
