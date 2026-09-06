using Minamo.Compiler;
using Minamo.Debug;
using System.Collections.Generic;
using Minamo.Codegen;
using Minamo.Runtime.Types.Functions;

namespace Minamo.Runtime.Types;

public interface IProduction
{
    string Constructor { get; }
}

public sealed class MinamoExceptionObject : MinamoObject, IProduction
{
    public string Name { get; }

    public string Message { get; }

    public MinamoTuple Data { get; }

    internal CallStackTrace? Trace { get; private set; }

    public string Constructor => Name;

    public override string TypeName => nameof(MinamoTypeCodes.Exception);

    public MinamoExceptionObject(string name, string message, MinamoTuple data) : base(MinamoTypeCodes.Exception) =>
        (Name, Message, Data) = (name, message, data);

    internal MinamoExceptionObject WithTrace(CallStackTrace? trace)
    {
        Trace = trace;
        return this;
    }

    public override object ToObject() => this;

    public override MinamoObject Clone()
    {
        var clone = new MinamoExceptionObject(Name, Message, Data);
        clone.Trace = Trace;
        return clone;
    }

    public override bool Equals(MinamoObject? other) =>
        other is MinamoExceptionObject ex
        && ex.Name == Name
        && ex.Message == Message
        && ex.Data.Equals(Data);

    public override int GetHashCode() => HashCode.Combine(Name, Message, Data);

    public override string ToString() => $"{Name}: {Message}";
}

[MinamoType]
internal sealed partial class MinamoExceptionTypeInfo : MinamoTypeInfo
{
    public override string ReflectedTypeName => nameof(MinamoTypeCodes.Exception);

    public override int ReflectedTypeId => MinamoTypeCodes.Exception;

    public MinamoExceptionTypeInfo()
    {
        AddMixins(MinamoTypeCodes.Equatable);
        SetSupportedOperations(Ops.Len);
    }

    protected override MinamoObject EqOp(ExecutionContext ctx, MinamoObject left, MinamoObject right) =>
        left.TypeId == right.TypeId && left.Equals(right) ? True : False;

    protected override MinamoObject ToStringOp(ExecutionContext ctx, MinamoObject arg, MinamoObject format) =>
        new MinamoString(arg.ToString());

    protected override MinamoObject LengthOp(ExecutionContext ctx, MinamoObject arg) =>
        new MinamoInteger(((MinamoExceptionObject)arg).Data.Count);

    protected override MinamoObject GetOp(ExecutionContext ctx, MinamoObject self, MinamoObject index) =>
        ((MinamoExceptionObject)self).Data.GetItem(ctx, index);

    [MinamoProperty("Name")]
    internal static string GetName(MinamoExceptionObject self) => self.Name;

    [MinamoProperty("Message")]
    internal static string GetMessage(MinamoExceptionObject self) => self.Message;

    [MinamoProperty("Data")]
    internal static MinamoObject GetData(MinamoExceptionObject self) =>
        self.Data.Count == 0 ? Nil : self.Data;

    [MinamoProperty("StackTrace")]
    internal static MinamoObject GetStackTrace(MinamoExceptionObject self) =>
        self.Trace is null ? Nil : new MinamoString(self.Trace.ToString());

    protected override MinamoFunction? InitializeStaticMember(string name, ExecutionContext ctx)
    {
        if (!char.IsUpper(name[0]))
        {
            return base.InitializeStaticMember(name, ctx);
        }

        return new MinamoExceptionConstructor(name, (_, args) => ErrorGenerators.RuntimeException(name, args), new("values", ParKind.VarArg));
    }
}
