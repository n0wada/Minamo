using Spellkit.Runtime.Types;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CatchMarks = System.Collections.Generic.Stack<Spellkit.Runtime.CatchMark>;

namespace Spellkit.Runtime;

internal sealed class CallStack : IEnumerable<Caller>
{
    private const int DefaultSize = 6;

    private readonly int initialSize;
    private Caller[] array;

    public int Count { get; private set; }

    public Caller this[int index]
    {
        get => array[index];
        set => array[index] = value;
    }

    public CallStack() : this(DefaultSize) { }

    private CallStack(int size) => (initialSize, array) = (size, new Caller[size]);

    public IEnumerator<Caller> GetEnumerator() => array.Take(Count).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public CallStack Clone() => (CallStack)MemberwiseClone();

    public void Clear() => (Count, array) = (0, new Caller[initialSize]);

    public Caller Pop() => Count == 0 ? throw new IndexOutOfRangeException() : array[--Count];

    public bool PopLast()
    {
        array[--Count] = null!;
        return true;
    }

    public Caller Peek() => array[Count - 1];

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

internal sealed class Caller
{
    public static readonly Caller Root = new();
    public static readonly Caller External = new();

    public readonly SpellkitObject[] Locals;
    public readonly EvalStack EvalStack;
    public readonly int Offset;
    public readonly SpellkitNativeFunction Function;

    private Caller()
    {
        Locals = Array.Empty<SpellkitObject>();
        EvalStack = new(0);
        Function = new(null, 0, 0, FastList<SpellkitObject[]>.Empty, 0);
    }

    public Caller(SpellkitNativeFunction function, int offset, EvalStack evalStack, SpellkitObject[] locals) =>
        (Function, Offset, EvalStack, Locals) = (function, offset, evalStack, locals);
}

internal sealed class EvalStack
{
    private readonly SpellkitObject[] array;
    private int size;

    internal int Size => size;

    public EvalStack(int size) => array = new SpellkitObject[size];

    internal void Dup() => array[size++] = array[size - 2];

    internal SpellkitObject Pop()
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

    internal SpellkitObject Peek() => array[size - 1];

    internal SpellkitObject Peek(int n) => array[size - n];

    internal void Push(SpellkitObject val) => array[size++] = val;

    internal void Replace(SpellkitObject val) => array[size - 1] = val;

    internal void Push(bool val) => array[size++] = val ? True : False;

    internal void Replace(bool val) => array[size - 1] = val ? True : False;
}

internal readonly record struct CatchMark(int Offset, int StackOffset);

internal sealed class SectionStack : IEnumerable<CatchMarks>
{
    private const int DefaultSize = 4;
    private CatchMarks[] array;
    private readonly int initialSize;

    public int Count { get; private set; }

    public SectionStack() : this(DefaultSize) { }

    public SectionStack(int size)
    {
        initialSize = size;
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

    public void Clear()
    {
        Count = 0;
        array = new CatchMarks[initialSize];
    }

    public CatchMarks Pop() => Count == 0 ? throw new IndexOutOfRangeException() : array[--Count];

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

    public CatchMarks this[int index]
    {
        get => array[index];
        set => array[index] = value;
    }
}
