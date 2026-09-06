using Minamo.Hosting;
using Minamo.Parser.Model;

namespace Minamo.UnitTesting;

internal static class HostingTestExtensions
{
    internal static MinamoExecutionResult Execute(
        this MinamoInstance instance,
        CancellationToken cancellationToken = default) =>
        instance.ExecuteAsync(cancellationToken).GetAwaiter().GetResult();

    internal static MinamoExecutionResult Execute(
        this MinamoInstance instance,
        string source,
        CancellationToken cancellationToken = default) =>
        instance.ExecuteAsync(source, cancellationToken).GetAwaiter().GetResult();

    internal static MinamoExecutionResult Execute(
        this MinamoInstance instance,
        MinamoCodeModel model,
        CancellationToken cancellationToken = default) =>
        instance.ExecuteAsync(model, cancellationToken).GetAwaiter().GetResult();

    internal static MinamoExecutionResult ExecuteFile(
        this MinamoInstance instance,
        string fileName,
        CancellationToken cancellationToken = default) =>
        instance.ExecuteFileAsync(fileName, cancellationToken).GetAwaiter().GetResult();

    internal static MinamoSelectSession OpenSelectSession(
        this MinamoInstance instance,
        string name) =>
        instance.OpenSelectSessionAsync(name).GetAwaiter().GetResult();

    internal static MinamoSignalDispatchResult DispatchSignals(
        this MinamoInstance instance,
        CancellationToken cancellationToken = default) =>
        instance.DispatchSignalsAsync(cancellationToken).GetAwaiter().GetResult();
}
