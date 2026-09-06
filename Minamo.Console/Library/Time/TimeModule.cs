using Minamo.Hosting;
using Minamo.Linker;

namespace Minamo.Library.Time;

[MinamoModule("time")]
public sealed class TimeModule : ForeignUnit
{
    public MinamoDateTimeTypeInfo DateTime { get; }

    public MinamoLocalDateTimeTypeInfo LocalDateTime { get; }

    public MinamoTimeDeltaTypeInfo TimeDelta { get; }

    public MinamoCalendarTypeInfo Calendar { get; }

    public MinamoTimeTypeInfo Time { get; }

    public MinamoDateTypeInfo Date { get; }

    public TimeModule()
    {
        DateTime = AddType<MinamoDateTimeTypeInfo>();
        LocalDateTime = AddType<MinamoLocalDateTimeTypeInfo>();
        TimeDelta = AddType<MinamoTimeDeltaTypeInfo>();
        Calendar = AddType<MinamoCalendarTypeInfo>();
        Time = AddType<MinamoTimeTypeInfo>();
        Date = AddType<MinamoDateTypeInfo>();
    }
}
