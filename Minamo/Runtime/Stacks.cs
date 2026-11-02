using Minamo.Runtime.Types;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CatchMarks = System.Collections.Generic.Stack<Minamo.Runtime.CatchMark>;

namespace Minamo.Runtime;

internal sealed class CallStack : IEnumerable<Caller>
{
    private const int DefaultSize = 6;

    private Caller[] array;

    public int Count { get; private set; }

    public Caller this[int index] => array[index];

    public CallStack() : this(DefaultSize) { }

    private CallStack(int size) => array = new Caller[size];

    public IEnumerator<Caller> GetEnumerator() => array.Take(Count).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public CallStack Clone()
    {
        var clone = new CallStack(Math.Max(DefaultSize, Count)) { Count = Count };
        Array.Copy(array, clone.array, Count);
        return clone;
    }

    public Caller Pop()
    {
        if (Count == 0) throw new IndexOutOfRangeException();
        var value = array[--Count];
        array[Count] = default;
        return value;
    }

    public void Push(Caller val)
    {
        if (Count == array.Length)
        {
            var dest = new Caller[array.Length * 2];

            for (var i = 0; i < Count; i++)
            {
                dest[i] = array[i];
            }

            array = dest;
        }

        array[Count++] = val;
    }
}

internal readonly struct Caller
{
    public static readonly Caller Root = new(1);
    public static readonly Caller External = new(2);
    private readonly byte kind;
    public bool IsRoot => kind == 1;
    public bool IsExternal => kind == 2;

    public readonly MinamoObject[] Locals;
    public readonly EvalStack EvalStack;
    public readonly int Offset;
    public readonly MinamoNativeFunction Function;

    private Caller(int kind)
    {
        this.kind = (byte)kind;
        Locals = null!;
        EvalStack = null!;
        Function = null!;
        Offset = 0;
    }

    public Caller(MinamoNativeFunction function, int offset, EvalStack evalStack, MinamoObject[] locals) =>
        (Function, Offset, EvalStack, Locals, kind) = (function, offset, evalStack, locals, 0);
}

internal sealed class EvalStack
{
    private readonly MinamoObject[] array;
    private int size;

    internal int Size => size;

    public EvalStack(int size) => array = new MinamoObject[size];

    internal void Dup() => array[size++] = array[size - 2];

    internal MinamoObject Pop()
    {
        var ret = array[--size];
        array[size] = null!;
        return ret;
    }

    internal void PopVoid() => array[--size] = null!;

    internal void Clear()
    {
        while (size > 0)
        {
            array[--size] = null!;
        }
    }

    internal MinamoObject Peek() => array[size - 1];

    internal MinamoObject Peek(int n) => array[size - n];

    internal void Push(MinamoObject val) => array[size++] = val;

    internal void Replace(MinamoObject val) => array[size - 1] = val;

    internal void Push(bool val) => array[size++] = val ? True : False;

    internal void Replace(bool val) => array[size - 1] = val ? True : False;
}

internal readonly record struct CatchMark(int Offset, int StackOffset);

internal sealed class SectionStack : IEnumerable<CatchMarks>
{
    private const int DefaultSize = 4;
    private CatchMarks[] array;

    public int Count { get; private set; }

    public SectionStack() : this(DefaultSize) { }

    public SectionStack(int size)
    {
        array = new CatchMarks[size];
    }

    public IEnumerator<CatchMarks> GetEnumerator()
    {
        var c = Count;

        while (c > 0)
        {
            yield return array[--c];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public CatchMarks Pop()
    {
        if (Count == 0) throw new IndexOutOfRangeException();
        var value = array[--Count];
        array[Count] = null!;
        return value;
    }

    public CatchMarks Peek() => array[Count - 1];

    public bool TryPeek(int i, out CatchMarks val)
    {
        if (Count - i < 0)
        {
            val = default!;
            return false;
        }

        val = array[Count - i];
        return true;
    }

    public void Push(CatchMarks val)
    {
        if (Count == array.Length)
        {
            var dest = new CatchMarks[array.Length * 2];

            for (var i = 0; i < Count; i++)
            {
                dest[i] = array[i];
            }

            array = dest;
        }

        array[Count++] = val;
    }

    public void Replace(CatchMarks val) => array[Count - 1] = val;
}
