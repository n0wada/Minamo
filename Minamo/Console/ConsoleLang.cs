using Minamo.Hosting;
using Minamo.Linker;
using Minamo.Runtime.Types;

namespace Minamo;

internal sealed class ConsoleLang : Lang
{
    internal ConsoleLang(MinamoTuple? arguments, bool exposeHostObject)
        : base(arguments, exposeHostObject) { }

    protected override void InitializeMembers()
    {
        base.InitializeMembers();
        Add("readLine", new MinamoExternalFunction("readLine", false, (context, _, _) =>
            MinamoCommandConvert.FromAwaitable(MinamoConsoleTypeInfo.ReadLine(context))));
    }

    protected override void InitializeTypes()
    {
        base.InitializeTypes();
        AddType<MinamoConsoleTypeInfo>();
    }
}
