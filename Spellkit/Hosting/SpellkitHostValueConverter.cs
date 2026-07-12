using Spellkit.Runtime;
using Spellkit.Runtime.Types;

namespace Spellkit.Hosting;

internal static class SpellkitHostValueConverter
{
    internal static bool TryConvert<T>(SpkObject? value, out T? result)
    {
        if (value is null)
        {
            result = default;
            return false;
        }

        if (value.TypeId == Spk.Nil)
        {
            result = default;
            return true;
        }

        if (TypeConverter.TryConvert(value, typeof(T), out var converted))
        {
            result = (T?)converted;
            return true;
        }

        result = default;
        return false;
    }

    internal static T? Convert<T>(SpkObject? value, string valueName)
    {
        if (value is null)
        {
            throw new InvalidOperationException($"{valueName} is not available.");
        }

        if (TryConvert<T>(value, out var result))
        {
            return result;
        }

        throw new InvalidCastException(
            $"{valueName} of type '{value.TypeName}' cannot be converted to '{typeof(T).FullName}'.");
    }
}
