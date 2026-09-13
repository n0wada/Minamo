using Minamo.Runtime;
using Minamo.Runtime.Types;
using System.Collections.Generic;
using System.Linq;

namespace Minamo.Hosting;

public sealed class MinamoRegistry : IDisposable
{
    private readonly Dictionary<string, MinamoObject> values = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Threading.Lock syncRoot = new();
    private bool disposed;

    public IReadOnlyCollection<string> Keys
    {
        get
        {
            lock (syncRoot)
            {
                ThrowIfDisposed();
                return values.Keys.ToArray();
            }
        }
    }

    public bool Contains(string key)
    {
        lock (syncRoot)
        {
            ThrowIfDisposed();
            return values.ContainsKey(key);
        }
    }

    internal MinamoObject? GetRaw(string key)
    {
        lock (syncRoot)
        {
            ThrowIfDisposed();
            return values.TryGetValue(key, out var value) ? value : null;
        }
    }

    public T? Get<T>(string key)
    {
        var value = GetRaw(key);
        if (value is null)
        {
            return default;
        }

        return MinamoHostValueConverter.Convert<T>(value, $"Registry value '{key}'");
    }

    public bool TryGet<T>(string key, out T? value)
    {
        MinamoObject raw;
        lock (syncRoot)
        {
            ThrowIfDisposed();
            if (!values.TryGetValue(key, out raw!))
            {
                value = default;
                return false;
            }
        }

        return MinamoHostValueConverter.TryConvert(raw, out value);
    }

    public void Set<T>(string key, T value) =>
        SetRaw(key, TypeConverter.ConvertFrom(value));

    internal void SetRaw(string key, MinamoObject value)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(value);
        lock (syncRoot)
        {
            ThrowIfDisposed();
            values[key] = value;
        }
    }

    public bool Remove(string key)
    {
        lock (syncRoot)
        {
            ThrowIfDisposed();
            return values.Remove(key);
        }
    }

    public void Clear()
    {
        lock (syncRoot)
        {
            ThrowIfDisposed();
            values.Clear();
        }
    }

    public void Dispose()
    {
        lock (syncRoot)
        {
            values.Clear();
            disposed = true;
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Registry keys cannot be empty.", nameof(key));
        }
    }
}
