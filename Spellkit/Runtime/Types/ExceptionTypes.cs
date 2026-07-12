using Spellkit.Compiler;
using Spellkit.Debug;
using System.Collections.Generic;
using Spellkit.Codegen;
using Spellkit.Runtime.Types.Functions;

namespace Spellkit.Runtime.Types;

public interface IProduction
{
    string Constructor { get; }
}

public sealed class SpkExceptionObject : SpkObject, IProduction
{
    public string Name { get; }

    public string Message { get; }

    public SpkTuple Data { get; }

    internal CallStackTrace? Trace { get; private set; }

    public string Constructor => Name;

    public override string TypeName => nameof(Spk.Exception);

    public SpkExceptionObject(string name, string message, SpkTuple data) : base(Spk.Exception) =>
        (Name, Message, Data) = (name, message, data);

    internal SpkExceptionObject WithTrace(CallStackTrace? trace)
    {
        Trace = trace;
        return this;
    }

    public override object ToObject() => this;

    public override SpkObject Clone()
    {
        var clone = new SpkExceptionObject(Name, Message, Data);
        clone.Trace = Trace;
        return clone;
    }

    public override bool Equals(SpkObject? other) =>
        other is SpkExceptionObject ex
        && ex.Name == Name
        && ex.Message == Message
        && ex.Data.Equals(Data);

    public override int GetHashCode() => HashCode.Combine(Name, Message, Data);

    public override string ToString() => $"{Name}: {Message}";
}

[SpkType]
internal sealed partial class SpkExceptionTypeInfo : SpkTypeInfo
{
    public override string ReflectedTypeName => nameof(Spk.Exception);

    public override int ReflectedTypeId => Spk.Exception;

    public SpkExceptionTypeInfo()
    {
        AddMixins(Spk.Equatable);
        SetSupportedOperations(Ops.Len);
    }

    protected override SpkObject EqOp(ExecutionContext ctx, SpkObject left, SpkObject right) =>
        left.TypeId == right.TypeId && left.Equals(right) ? True : False;

    protected override SpkObject ToStringOp(ExecutionContext ctx, SpkObject arg, SpkObject format) =>
        new SpkString(arg.ToString());

    protected override SpkObject LengthOp(ExecutionContext ctx, SpkObject arg) =>
        new SpkInteger(((SpkExceptionObject)arg).Data.Count);

    protected override SpkObject GetOp(ExecutionContext ctx, SpkObject self, SpkObject index) =>
        ((SpkExceptionObject)self).Data.GetItem(ctx, index);

    [SpkProperty("Name")]
    internal static string GetName(SpkExceptionObject self) => self.Name;

    [SpkProperty("Message")]
    internal static string GetMessage(SpkExceptionObject self) => self.Message;

    [SpkProperty("Data")]
    internal static SpkObject GetData(SpkExceptionObject self) =>
        self.Data.Count == 0 ? Nil : self.Data;

    [SpkProperty("StackTrace")]
    internal static SpkObject GetStackTrace(SpkExceptionObject self) =>
        self.Trace is null ? Nil : new SpkString(self.Trace.ToString());

    protected override SpkFunction? InitializeStaticMember(string name, ExecutionContext ctx)
    {
        if (!char.IsUpper(name[0]))
        {
            return base.InitializeStaticMember(name, ctx);
        }

        return new SpkExceptionConstructor(name, (_, args) => ErrorGenerators.RuntimeException(name, args), new("values", ParKind.VarArg));
    }
}
