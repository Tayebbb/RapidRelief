using Microsoft.Extensions.DependencyInjection;
using RapidRelief.Api.Features.Ai;
using RapidRelief.Api.Features.Auth.Services;
using RapidRelief.Api.Features.Realtime;
using RapidRelief.Api.Features.Stubs;
using RapidRelief.Api.Infrastructure.Eventing;
using RapidRelief.Api.Infrastructure.Storage;
using RapidRelief.Shared.Contracts.Eventing;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Tests.Smoke;

public sealed class DiResolutionSmokeTests : IClassFixture<TestingWebAppFactory>
{
    private readonly TestingWebAppFactory _factory;

    public DiResolutionSmokeTests(TestingWebAppFactory factory) => _factory = factory;

    [Fact]
    public void Factory_boots_and_resolves_scoped_event_bus()
    {
        using var scope = _factory.Services.CreateScope();

        var bus = scope.ServiceProvider.GetRequiredService<IEventBus>();

        Assert.IsType<InProcessEventBus>(bus);
    }

    [Fact]
    public void All_seven_contract_interfaces_resolve_to_the_expected_implementations()
    {
        // Proves module discovery + the stub-yield rule (TryAdd in StubsModule, plain Add in
        // real-service slots): fakes back the 3 read contracts; F1's real IdentityUserAdminService
        // has displaced FakeUserAdminService (blueprint ㉞); real fallbacks serve the rest.
        using var scope = _factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        // F2 + F5
        Assert.IsType<FakeIncidentReadService>(scope.ServiceProvider.GetRequiredService<IIncidentReadService>());

        // F3 real implementation now registered
        Assert.IsType<RapidRelief.Api.Features.Shelters.Services.ShelterReadService>(
            scope.ServiceProvider.GetRequiredService<IShelterReadService>());

        // F13
        Assert.IsType<FakeRegistryReadService>(scope.ServiceProvider.GetRequiredService<IRegistryReadService>());
        Assert.IsType<IdentityUserAdminService>(services.GetRequiredService<IUserAdminService>());
        Assert.IsType<RuleBasedAiAnalysisService>(services.GetRequiredService<IAiAnalysisService>());
        Assert.IsType<NoOpRealtimeNotifier>(services.GetRequiredService<IRealtimeNotifier>());
        Assert.IsType<LocalDiskFileStorage>(services.GetRequiredService<IFileStorage>());
    }
}
