using Spellkit.Codegen;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.Linq;

namespace Spellkit.Library.Binary;

[SpkType]
public sealed partial class SpkByteArrayTypeInfo : SpkForeignTypeInfo
{
    private const string ByteArray = nameof(ByteArray);

    public override string ReflectedTypeName => ByteArray;

    public SpkByteArrayTypeInfo()
    {
        SetSupportedOperations(Ops.Len);
    }

    #region Operations
    public SpkByteArray Create(byte[]? buffer) => new(this, buffer);

    protected override SpkObject ToStringOp(ExecutionContext ctx, SpkObject arg, SpkObject format)
    {
        var buffer = ((SpkByteArray)arg).GetBytes();
        var strs = buffer.Select(b => "0x" + b.ToString("X").PadLeft(2, '0')).ToArray();
        return new SpkString("{" + string.Join(",", strs) + "}");
    }

    protected override SpkObject LengthOp(ExecutionContext ctx, SpkObject arg) =>
        SpkInteger.Get(((SpkByteArray)arg).Count);
    #endregion

    [SpkMethod]
    internal static SpkObject Read(ExecutionContext ctx, SpkByteArray self, SpkTypeInfo typeInfo) => self.Read(ctx, typeInfo);

    [SpkMethod]
    internal static void Write(ExecutionContext ctx, SpkByteArray self, SpkObject value) => self.Write(ctx, value);

    [SpkMethod]
    internal static void Reset(SpkByteArray self) => self.Reset();

    [SpkProperty]
    internal static int Position(SpkByteArray self) => self.Position;

    [SpkStaticMethod]
    internal static SpkObject Concat(ExecutionContext ctx, SpkByteArray first, SpkByteArray second)
    {
        var a1 = first.GetBytes();
        var a2 = second.GetBytes();
        var a3 = new byte[a1.Length + a2.Length];
        Array.Copy(a1, a3, a1.Length);
        Array.Copy(a2, 0, a3, a1.Length, a2.Length);
        return new SpkByteArray(ctx.Type<SpkByteArrayTypeInfo>(), a3);
    }

    [SpkStaticMethod(ByteArray)]
    internal static SpkObject CreateNew(ExecutionContext ctx) => new SpkByteArray(ctx.Type<SpkByteArrayTypeInfo>(), null);
}
