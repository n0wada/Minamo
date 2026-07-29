using Spellkit.Hosting;
using Spellkit.Linker;

namespace Spellkit.Library.Time;

[SpellkitModule("time")]
public sealed class TimeModule : ForeignUnit
{
    public SpellkitDateTimeTypeInfo DateTime { get; }

    public SpellkitLocalDateTimeTypeInfo LocalDateTime { get; }

    public SpellkitTimeDeltaTypeInfo TimeDelta { get; }

    public SpellkitCalendarTypeInfo Calendar { get; }

    public SpellkitTimeTypeInfo Time { get; }

    public SpellkitDateTypeInfo Date { get; }

    public TimeModule()
    {
        DateTime = AddType<SpellkitDateTimeTypeInfo>();
        LocalDateTime = AddType<SpellkitLocalDateTimeTypeInfo>();
        TimeDelta = AddType<SpellkitTimeDeltaTypeInfo>();
        Calendar = AddType<SpellkitCalendarTypeInfo>();
        Time = AddType<SpellkitTimeTypeInfo>();
        Date = AddType<SpellkitDateTypeInfo>();
    }
}
