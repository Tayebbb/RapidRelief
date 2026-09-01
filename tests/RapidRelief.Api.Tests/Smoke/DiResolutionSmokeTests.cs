using Microsoft.Extensions.DependencyInjection;
using RapidRelief.Api.Infrastructure.Eventing;
using RapidRelief.Shared.Contracts.Eventing;

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
}
