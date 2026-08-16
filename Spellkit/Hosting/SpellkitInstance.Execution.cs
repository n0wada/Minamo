using Spellkit.Compiler;
using Spellkit.Runtime;
using System.Threading;
using System.Threading.Tasks;

namespace Spellkit.Hosting;

public sealed partial class SpellkitInstance
{
    private async Task<SpellkitRunSession> StartCoreAsync(
        Func<Result<UnitComposition>> compile)
    {
        if (operationScope.Value)
        {
            throw new InvalidOperationException("A host instance cannot be entered recursively.");
        }

        await operationGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        operationScope.Value = true;
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            BeginOperation();
            var linkerTouched = false;
            try
            {
                linkerTouched = true;
                var made = await Task.Run(compile, CancellationToken.None).ConfigureAwait(false);
                if (!made.Success || made.Value is null)
                {
                    TryRollback();
                    return new SpellkitRunSession(this, new InvalidOperationException(
                        string.Join(System.Environment.NewLine, made.Messages)));
                }

                var context = CreateExecutionContext(made.Value, control: null);
                var result = await Task.Run(
                    () => SpellkitMachine.Execute(context),
                    CancellationToken.None).ConfigureAwait(false);
                result = await CompleteAwaitablesAsync(result).ConfigureAwait(false);
                linker.Commit();
                var run = new SpellkitRunSession(this, result);
                await run.AdvanceAsync(result).ConfigureAwait(false);
                if (!run.IsCompleted)
                {
                    suspendedRun = run;
                }

                return run;
            }
            catch (Exception ex)
            {
                if (linkerTouched)
                {
                    TryRollback();
                }

                return new SpellkitRunSession(this, ex);
            }
            finally
            {
                active = false;
            }
        }
        finally
        {
            operationScope.Value = false;
            operationGate.Release();
        }
    }
}
