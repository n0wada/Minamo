using Spellkit.Hosting;
using Spellkit.Linker;

namespace Spellkit.Library.Time;

[SpellkitModule("time")]
public sealed class TimeModule : ForeignUnit
{
    public SpkDateTimeTypeInfo DateTime { get; }

    public SpkLocalDateTimeTypeInfo LocalDateTime { get; }

    public SpkTimeDeltaTypeInfo TimeDelta { get; }

    public SpkCalendarTypeInfo Calendar { get; }

    public SpkTimeTypeInfo Time { get; }

    public SpkDateTypeInfo Date { get; }

    public TimeModule()
    {
        DateTime = AddType<SpkDateTimeTypeInfo>();
        LocalDateTime = AddType<SpkLocalDateTimeTypeInfo>();
        TimeDelta = AddType<SpkTimeDeltaTypeInfo>();
        Calendar = AddType<SpkCalendarTypeInfo>();
        Time = AddType<SpkTimeTypeInfo>();
        Date = AddType<SpkDateTypeInfo>();
    }
}
