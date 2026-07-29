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

public sealed class SpellkitExceptionObject : SpellkitObject, IProduction
{
    public string Name { get; }

    public string Message { get; }

    public SpellkitTuple Data { get; }

    internal CallStackTrace? Trace { get; private set; }

    public string Constructor => Name;

    public override string TypeName => nameof(SpellkitTypeCodes.Exception);

    public SpellkitExceptionObject(string name, string message, SpellkitTuple data) : base(SpellkitTypeCodes.Exception) =>
        (Name, Message, Data) = (name, message, data);

    internal SpellkitExceptionObject WithTrace(CallStackTrace? trace)
    {
        Trace = trace;
        return this;
    }

    public override object ToObject() => this;

    public override SpellkitObject Clone()
    {
        var clone = new SpellkitExceptionObject(Name, Message, Data);
        clone.Trace = Trace;
        return clone;
    }

    public override bool Equals(SpellkitObject? other) =>
        other is SpellkitExceptionObject ex
        && ex.Name == Name
        && ex.Message == Message
        && ex.Data.Equals(Data);

    public override int GetHashCode() => HashCode.Combine(Name, Message, Data);

    public override string ToString() => $"{Name}: {Message}";
}

[SpellkitType]
internal sealed partial class SpellkitExceptionTypeInfo : SpellkitTypeInfo
{
    public override string ReflectedTypeName => nameof(SpellkitTypeCodes.Exception);

    public override int ReflectedTypeId => SpellkitTypeCodes.Exception;

    public SpellkitExceptionTypeInfo()
    {
        AddMixins(SpellkitTypeCodes.Equatable);
        SetSupportedOperations(Ops.Len);
    }

    protected override SpellkitObject EqOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right) =>
        left.TypeId == right.TypeId && left.Equals(right) ? True : False;

    protected override SpellkitObject ToStringOp(ExecutionContext ctx, SpellkitObject arg, SpellkitObject format) =>
        new SpellkitString(arg.ToString());

    protected override SpellkitObject LengthOp(ExecutionContext ctx, SpellkitObject arg) =>
        new SpellkitInteger(((SpellkitExceptionObject)arg).Data.Count);

    protected override SpellkitObject GetOp(ExecutionContext ctx, SpellkitObject self, SpellkitObject index) =>
        ((SpellkitExceptionObject)self).Data.GetItem(ctx, index);

    [SpellkitProperty("Name")]
    internal static string GetName(SpellkitExceptionObject self) => self.Name;

    [SpellkitProperty("Message")]
    internal static string GetMessage(SpellkitExceptionObject self) => self.Message;

    [SpellkitProperty("Data")]
    internal static SpellkitObject GetData(SpellkitExceptionObject self) =>
        self.Data.Count == 0 ? Nil : self.Data;

    [SpellkitProperty("StackTrace")]
    internal static SpellkitObject GetStackTrace(SpellkitExceptionObject self) =>
        self.Trace is null ? Nil : new SpellkitString(self.Trace.ToString());

    protected override SpellkitFunction? InitializeStaticMember(string name, ExecutionContext ctx)
    {
        if (!char.IsUpper(name[0]))
        {
            return base.InitializeStaticMember(name, ctx);
        }

        return new SpellkitExceptionConstructor(name, (_, args) => ErrorGenerators.RuntimeException(name, args), new("values", ParKind.VarArg));
    }
}
