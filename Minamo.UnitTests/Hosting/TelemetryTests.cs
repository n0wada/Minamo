using Xunit;

namespace Minamo.UnitTesting.Hosting;

[Trait("Suite", "Hosting")]
public sealed class TelemetryTests
{
    [Fact]
    public void CapturesStructuredLogs() =>
        HostingScenarios.Logs();

    [Fact]
    public void IsolatesCorrelationByExecutionContext() =>
        HostingScenarios.TelemetryExecutionContextIsolation();
}
