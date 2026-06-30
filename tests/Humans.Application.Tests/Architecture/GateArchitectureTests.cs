using AwesomeAssertions;
using Humans.Application.Interfaces.Repositories;
using Humans.Application.Interfaces.Tickets;
using Humans.Application.Services.Gate;

namespace Humans.Application.Tests.Architecture;

/// <summary>
/// Pins the new Gate (admissions) section's shape: an Application-layer service in
/// the Gate namespace, all DB access via <see cref="IGateRepository"/> (no
/// DbContext), and cross-section ticket reads via the read interface
/// (<see cref="ITicketServiceRead"/>, not the full <c>ITicketService</c>).
/// </summary>
public class GateArchitectureTests
{
    private static System.Reflection.ConstructorInfo Ctor =>
        typeof(GateService).GetConstructors().Single();

    [HumansFact]
    public void GateService_LivesInGateNamespace() =>
        typeof(GateService).Namespace.Should().Be("Humans.Application.Services.Gate");

    [HumansFact]
    public void GateService_GoesThroughGateRepository() =>
        Ctor.GetParameters().Select(p => p.ParameterType).Should().Contain(typeof(IGateRepository));

    [HumansFact]
    public void GateService_TakesNoDbContext() =>
        Ctor.GetParameters()
            .Select(p => p.ParameterType.Namespace ?? string.Empty)
            .Should().NotContain(ns => ns.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));

    [HumansFact]
    public void GateService_ReadsTicketsViaReadInterface()
    {
        var paramTypes = Ctor.GetParameters().Select(p => p.ParameterType).ToList();
        paramTypes.Should().Contain(typeof(ITicketServiceRead));
        paramTypes.Should().NotContain(typeof(ITicketService),
            because: "cross-section ticket reads must use the read interface (section-read-write-split / HUM0032)");
    }
}
