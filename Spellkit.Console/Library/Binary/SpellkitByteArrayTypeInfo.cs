using Spellkit.Codegen;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.Linq;

namespace Spellkit.Library.Binary;

[SpellkitType]
public sealed partial class SpellkitByteArrayTypeInfo : SpellkitForeignTypeInfo
{
    private const string ByteArray = nameof(ByteArray);

    public override string ReflectedTypeName => ByteArray;

    public SpellkitByteArrayTypeInfo()
    {
        SetSupportedOperations(Ops.Len);
    }

    #region Operations
    public SpellkitByteArray Create(byte[]? buffer) => new(this, buffer);

    protected override SpellkitObject ToStringOp(ExecutionContext ctx, SpellkitObject arg, SpellkitObject format)
    {
        var buffer = ((SpellkitByteArray)arg).GetBytes();
        var strs = buffer.Select(b => "0x" + b.ToString("X").PadLeft(2, '0')).ToArray();
        return new SpellkitString("{" + string.Join(",", strs) + "}");
    }

    protected override SpellkitObject LengthOp(ExecutionContext ctx, SpellkitObject arg) =>
        SpellkitInteger.Get(((SpellkitByteArray)arg).Count);
    #endregion

    [SpellkitMethod]
    internal static SpellkitObject Read(ExecutionContext ctx, SpellkitByteArray self, SpellkitTypeInfo typeInfo) => self.Read(ctx, typeInfo);

    [SpellkitMethod]
    internal static void Write(ExecutionContext ctx, SpellkitByteArray self, SpellkitObject value) => self.Write(ctx, value);

    [SpellkitMethod]
    internal static void Reset(SpellkitByteArray self) => self.Reset();

    [SpellkitProperty]
    internal static int Position(SpellkitByteArray self) => self.Position;

    [SpellkitStaticMethod]
    internal static SpellkitObject Concat(ExecutionContext ctx, SpellkitByteArray first, SpellkitByteArray second)
    {
        var a1 = first.GetBytes();
        var a2 = second.GetBytes();
        var a3 = new byte[a1.Length + a2.Length];
        Array.Copy(a1, a3, a1.Length);
        Array.Copy(a2, 0, a3, a1.Length, a2.Length);
        return new SpellkitByteArray(ctx.Type<SpellkitByteArrayTypeInfo>(), a3);
    }

    [SpellkitStaticMethod(ByteArray)]
    internal static SpellkitObject CreateNew(ExecutionContext ctx) => new SpellkitByteArray(ctx.Type<SpellkitByteArrayTypeInfo>(), null);
}
