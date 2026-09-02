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

        Assert.IsType<FakeIncidentReadService>(services.GetRequiredService<IIncidentReadService>());
        Assert.IsType<FakeShelterReadService>(services.GetRequiredService<IShelterReadService>());
        Assert.IsType<FakeRegistryReadService>(services.GetRequiredService<IRegistryReadService>());
        Assert.IsType<IdentityUserAdminService>(services.GetRequiredService<IUserAdminService>());
        // F8: the OpenRouter-with-fallback composite displaces the direct rule-based binding
        // (D-028); the rule-based service stays resolvable concretely — the fallback never dies (§4.5).
        Assert.IsType<OpenRouterAiAnalysisService>(services.GetRequiredService<IAiAnalysisService>());
        Assert.IsType<RuleBasedAiAnalysisService>(services.GetRequiredService<RuleBasedAiAnalysisService>());
        // F9: the SignalR notifier displaces the no-op in Mode=Hub/PollingOnly (D-032); the
        // no-op stays resolvable concretely and is the binding again in Mode=Off (§4.5).
        Assert.IsType<SignalRRealtimeNotifier>(services.GetRequiredService<IRealtimeNotifier>());
        Assert.IsType<NoOpRealtimeNotifier>(services.GetRequiredService<NoOpRealtimeNotifier>());
        Assert.IsType<LocalDiskFileStorage>(services.GetRequiredService<IFileStorage>());
    }
}
