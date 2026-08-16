using Spellkit.Hosting;
using Spellkit.Parser.Model;

namespace Spellkit.UnitTesting;

internal static class HostingTestExtensions
{
    internal static SpellkitExecutionResult Execute(
        this SpellkitInstance instance,
        CancellationToken cancellationToken = default) =>
        instance.ExecuteAsync(cancellationToken).GetAwaiter().GetResult();

    internal static SpellkitExecutionResult Execute(
        this SpellkitInstance instance,
        string source,
        CancellationToken cancellationToken = default) =>
        instance.ExecuteAsync(source, cancellationToken).GetAwaiter().GetResult();

    internal static SpellkitExecutionResult Execute(
        this SpellkitInstance instance,
        SpellkitCodeModel model,
        CancellationToken cancellationToken = default) =>
        instance.ExecuteAsync(model, cancellationToken).GetAwaiter().GetResult();

    internal static SpellkitExecutionResult ExecuteFile(
        this SpellkitInstance instance,
        string fileName,
        CancellationToken cancellationToken = default) =>
        instance.ExecuteFileAsync(fileName, cancellationToken).GetAwaiter().GetResult();

    internal static SpellkitSelectSession OpenSelectSession(
        this SpellkitInstance instance,
        string name) =>
        instance.OpenSelectSessionAsync(name).GetAwaiter().GetResult();

    internal static SpellkitRunSession Start(
        this SpellkitInstance instance,
        string source) =>
        instance.StartAsync(source).GetAwaiter().GetResult();

    internal static SpellkitSignalDispatchResult DispatchSignals(
        this SpellkitInstance instance,
        CancellationToken cancellationToken = default) =>
        instance.DispatchSignalsAsync(cancellationToken).GetAwaiter().GetResult();
}
