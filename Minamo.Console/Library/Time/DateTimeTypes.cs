using Minamo.Codegen;
using Minamo.Runtime;
using Minamo.Runtime.Types;
using System;
using System.Text;

namespace Minamo.Library.Time;

internal static class DT
{
    public const long TicksPerDay = 24 * TicksPerHour;
    public const long TicksPerHour = 60 * TicksPerMinute;
    public const long TicksPerMinute = 60 * TicksPerSecond;
    public const long TicksPerSecond = 10 * TicksPerDecisecond;
    public const long TicksPerDecisecond = 10 * TicksPerCentisecond;
    public const long TicksPerCentisecond = 10 * TicksPerMillisecond;
    public const long TicksPerMillisecond = 1000 * TicksPerMicrosecond;
    public const long TicksPerMicrosecond = 10L;

    public static long Sum(long days, long hours, long minutes, long sec, long ms) =>
        days * TimeSpan.TicksPerDay + hours * TimeSpan.TicksPerHour + minutes * TimeSpan.TicksPerMinute
        + sec * TimeSpan.TicksPerSecond + ms * TimeSpan.TicksPerMillisecond;
}

public interface ISpan
{
    long TotalTicks { get; }

    long ToInteger();
}

public interface IDate : ISpan
{
    int Year { get; }

    int Month { get; }

    int Day { get; }

    int DayOfYear { get; }

    string DayOfWeek { get; }

    void AddDays(int value);

    void AddMonths(int value);

    void AddYears(int value);
}

public interface IDateTime : IDate, ITime
{
    MinamoObject GetDate(MinamoDateTypeInfo typeInfo);

    MinamoObject GetTime(MinamoTimeTypeInfo typeInfo);

    void AddHours(double value);

    void AddMinutes(double value);

    void AddSeconds(double value);

    void AddMilliseconds(double value);

    void AddTicks(long value);
}

public interface ITime : ISpan
{
    int Hours { get; }

    int Minutes { get; }

    int Seconds { get; }

    int Milliseconds { get; }

    int Microseconds { get; }

    int Ticks { get; }
}

public interface IInterval : ITime
{
    int Days { get; }
}

public interface ILocalDateTime : IDateTime
{
    IInterval Interval { get; }
}

public abstract class SpanTypeInfo<T> : MinamoForeignTypeInfo<TimeModule>
    where T : MinamoObject, ISpan, IFormattable
{
    public override string ReflectedTypeName { get; }

    protected SpanTypeInfo(string typeName)
    {
        ReflectedTypeName = typeName;
        AddMixins(MinamoTypeCodes.Order, MinamoTypeCodes.Equatable);
    }

    #region Operations
    protected override MinamoObject ToStringOp(ExecutionContext ctx, MinamoObject arg, MinamoObject format)
    {
        if (format.Is(MinamoTypeCodes.Nil))
        {
            return new MinamoString(arg.ToString());
        }

        if (format.TypeId is not MinamoTypeCodes.String and not MinamoTypeCodes.Char)
        {
            return Nil;
        }

        try
        {
            return new MinamoString(((T)arg).ToString(format.ToString(), null));
        }
        catch (FormatException)
        {
            return ctx.ParsingFailed();
        }
    }

    protected override MinamoObject EqOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (right.TypeId != left.TypeId)
        {
            return MinamoBool.False;
        }

        return ((T)left).TotalTicks == ((T)right).TotalTicks ? True : False;
    }

    protected override MinamoObject NeqOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (right.TypeId != left.TypeId)
        {
            return MinamoBool.True;
        }

        return ((T)left).TotalTicks != ((T)right).TotalTicks ? True : False;
    }

    protected override MinamoObject GtOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (right.TypeId != left.TypeId)
        {
            return ctx.InvalidType(left.TypeId, right);
        }

        return ((T)left).TotalTicks > ((T)right).TotalTicks ? True : False;
    }

    protected override MinamoObject LtOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (right.TypeId != left.TypeId)
        {
            return ctx.InvalidType(left.TypeId, right);
        }

        return ((T)left).TotalTicks < ((T)right).TotalTicks ? True : False;
    }

    protected override MinamoObject GteOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (right.TypeId != left.TypeId)
        {
            return ctx.InvalidType(left.TypeId, right);
        }

        return ((T)left).TotalTicks >= ((T)right).TotalTicks ? True : False;
    }

    protected override MinamoObject LteOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (right.TypeId != left.TypeId)
        {
            return ctx.InvalidType(left.TypeId, right);
        }

        return ((T)left).TotalTicks <= ((T)right).TotalTicks ? True : False;
    }

    protected override MinamoObject CastOp(ExecutionContext ctx, MinamoObject self, MinamoTypeInfo targetType) =>
       targetType.ReflectedTypeId switch
       {
           MinamoTypeCodes.Integer => MinamoInteger.Get(((T)self).ToInteger()),
           _ => base.CastOp(ctx, self, targetType)
       };
    #endregion
}

public sealed class MinamoDate : MinamoForeignObject, IDate, IFormattable
{
    private const string DEFAULT_FORMAT = "yyyy-MM-dd";

    private int days;

    public MinamoDate(MinamoDateTypeInfo typeInfo, int days) : base(typeInfo) => this.days = days;

    public MinamoDate(MinamoDateTypeInfo typeInfo, DateTime dateTime) : this(typeInfo, DateOnly.FromDateTime(dateTime).DayNumber) { }

    public long TotalTicks => days * DT.TicksPerDay;

    public int Year => new DateTime(TotalTicks).Year;

    public int Month => new DateTime(TotalTicks).Month;

    public int Day => new DateTime(TotalTicks).Day;

    public string DayOfWeek => new DateTime(TotalTicks).DayOfWeek.ToString();

    public int DayOfYear => new DateTime(TotalTicks).DayOfYear;

    public override object ToObject() => new DateOnly(Year, Month, Day);

    public long ToInteger() => days;

    public override MinamoObject Clone() => new MinamoDate((MinamoDateTypeInfo)TypeInfo, days);

    public override int GetHashCode() => days.GetHashCode();

    public override bool Equals(MinamoObject? other) => other is MinamoDate dt && dt.days == days;

    public void AddDays(int days) => SetDays(new DateTime(TotalTicks).AddDays(days).Date);

    public void AddMonths(int months) => SetDays(new DateTime(TotalTicks).AddMonths(months).Date);

    public void AddYears(int years) => SetDays(new DateTime(TotalTicks).AddYears(years).Date);

    public static MinamoDate Parse(MinamoDateTypeInfo typeInfo, string format, string value)
    {
        var (ticks, _, _) = InputParser.Parse(FormatParser.DateParser, format, value);
        return new(typeInfo, (int)(ticks / DT.TicksPerDay));
    }

    public string ToString(string? format, IFormatProvider? _ = null)
    {
        var formats = FormatParser.DateParser.ParseSpecifiers(format ?? DEFAULT_FORMAT);
        var sb = new StringBuilder();

        foreach (var f in formats)
        {
            Formatter.FormatDate(this, sb, f);
        }

        return sb.ToString();
    }

    private void SetDays(DateTime dt) => days = DateOnly.FromDateTime(dt).DayNumber;

    public override string ToString() => ToString(DEFAULT_FORMAT);
}

public class MinamoDateTime : MinamoForeignObject, IDateTime, IFormattable
{
    private const string FORMAT = "yyyy-MM-dd HH:mm:ss.fffffff";

    protected long ticks;

    internal MinamoDateTime(SpanTypeInfo<MinamoDateTime> typeInfo, long ticks) : base(typeInfo) =>
        this.ticks = ticks;

    public DateTime ToDateTime() => new(ticks, DateTimeKind.Utc);

    public override object ToObject() => ToDateTime();

    public override MinamoObject Clone() => new MinamoDateTime((SpanTypeInfo<MinamoDateTime>)TypeInfo, ticks);

    public override bool Equals(MinamoObject? other) => other is MinamoDateTime dt && dt.ticks == ticks;

    public override int GetHashCode() => ticks.GetHashCode();

    public override string ToString() => ToString(FORMAT);

    public long ToInteger() => ticks;

    public static MinamoDateTime Parse(MinamoForeignTypeInfo typeInfo, string format, string value)
    {
        var (ticks, _, _) = InputParser.Parse(FormatParser.DateTimeParser, format, value);
        return new((SpanTypeInfo<MinamoDateTime>)typeInfo, ticks);
    }

    public virtual string ToString(string? format, IFormatProvider? _ = null)
    {
        var formats = FormatParser.DateTimeParser.ParseSpecifiers(format ?? FORMAT);
        var sb = new StringBuilder();

        foreach (var f in formats)
        {
            Formatter.FormatDateTime(this, sb, f);
        }

        return sb.ToString();
    }

    public virtual MinamoDateTime FirstDayOfMonth()
    {
        var dt = new DateTime(ticks, DateTimeKind.Unspecified);
        return new MinamoDateTime((SpanTypeInfo<MinamoDateTime>)TypeInfo, dt.AddDays(-dt.Day + 1).Ticks);
    }

    public virtual MinamoDateTime LastDayOfMonth()
    {
        var dt = new DateTime(ticks, DateTimeKind.Unspecified);
        return new MinamoDateTime((SpanTypeInfo<MinamoDateTime>)TypeInfo, dt.AddDays(DateTime.DaysInMonth(dt.Year, dt.Month) - dt.Day).Ticks);
    }

    #region DateTime
    public long TotalTicks => ticks;

    public int Ticks => (int)(ticks % 10_000_000);

    public int Microseconds => (int)(ticks / DT.TicksPerMicrosecond % 1_000_000);

    public int Milliseconds => (int)(ticks / DT.TicksPerMillisecond % 1000);

    public int Seconds => (int)(ticks / DT.TicksPerSecond % 60);

    public int Minutes => (int)(ticks / DT.TicksPerMinute % 60);

    public int Hours => (int)(ticks / DT.TicksPerHour % 24);

    public int Year => new DateTime(ticks).Year;

    public int Month => new DateTime(ticks).Month;

    public int Day => new DateTime(ticks).Day;

    public string DayOfWeek => new DateTime(ticks).DayOfWeek.ToString();

    public int DayOfYear => new DateTime(ticks).DayOfYear;

    public void AddDays(int value) => SetTicks(new DateTime(ticks).AddDays(value));

    public void AddMonths(int value) => SetTicks(new DateTime(ticks).AddMonths(value));

    public void AddYears(int value) => SetTicks(new DateTime(ticks).AddYears(value));

    public void AddHours(double value) => SetTicks(new DateTime(ticks).AddHours(value));

    public void AddMinutes(double value) => SetTicks(new DateTime(ticks).AddMinutes(value));

    public void AddSeconds(double value) => SetTicks(new DateTime(ticks).AddSeconds(value));

    public void AddMilliseconds(double value) => SetTicks(new DateTime(ticks).AddMilliseconds(value));

    public void AddTicks(long value) => ticks += value;

    public MinamoObject GetDate(MinamoDateTypeInfo typeInfo) =>
        new MinamoDate(typeInfo, DateOnly.FromDateTime(new DateTime(ticks)).DayNumber);

    public MinamoObject GetTime(MinamoTimeTypeInfo typeInfo) =>
        new MinamoTime(typeInfo, TimeOnly.FromDateTime(new DateTime(ticks)).Ticks);

    private void SetTicks(DateTime dt) => ticks = dt.Ticks;
    #endregion
}

public sealed class MinamoLocalDateTime : MinamoDateTime, ILocalDateTime
{
    private const string FORMAT = "yyyy-MM-dd HH:mm:ss.fffffffzzz";

    public MinamoTimeDelta Offset { get; }

    IInterval ILocalDateTime.Interval => Offset;

    internal MinamoLocalDateTime(MinamoLocalDateTimeTypeInfo typeInfo, long ticks, MinamoTimeDelta offset)
        : base(typeInfo, ticks) => this.Offset = offset;

    public long UtcTicks => ticks - Offset.TotalTicks;

    public override bool Equals(MinamoObject? other) =>
        other is MinamoLocalDateTime dt && dt.UtcTicks == UtcTicks;

    public static new MinamoDateTime Parse(MinamoForeignTypeInfo typeInfo, string format, string value)
    {
        var ti = (MinamoLocalDateTimeTypeInfo)typeInfo;
        var (ticks, offset, hasOffset) =
            InputParser.Parse(FormatParser.LocalDateTimeParser, format, value);
        var resolvedOffset = hasOffset
            ? TimeSpan.FromTicks(offset)
            : TimeZoneInfo.Local.GetUtcOffset(new DateTime(ticks, DateTimeKind.Unspecified));
        return new MinamoLocalDateTime(ti, ticks,
            new MinamoTimeDelta(ti.TypeDeltaTypeInfo, resolvedOffset));
    }

    public override MinamoObject Clone() =>
        new MinamoLocalDateTime((MinamoLocalDateTimeTypeInfo)TypeInfo, ticks, Offset);

    public override int GetHashCode() => UtcTicks.GetHashCode();

    public override string ToString() => ToString(FORMAT);

    public override string ToString(string? format, IFormatProvider? _ = null)
    {
        var formats = FormatParser.LocalDateTimeParser.ParseSpecifiers(format ?? FORMAT);
        var sb = new StringBuilder();

        foreach (var f in formats)
        {
            Formatter.FormatLocalDateTime(this, sb, f);
        }

        return sb.ToString();
    }

    public override object ToObject() => ToDateTimeOffset();

    public DateTimeOffset ToDateTimeOffset() => new(new DateTime(ticks, DateTimeKind.Unspecified), Offset.ToTimeSpan());

    public override MinamoDateTime FirstDayOfMonth()
    {
        var dt = new DateTime(ticks, DateTimeKind.Unspecified);
        return new MinamoLocalDateTime((MinamoLocalDateTimeTypeInfo)TypeInfo, dt.AddDays(-dt.Day + 1).Ticks, Offset);
    }

    public override MinamoDateTime LastDayOfMonth()
    {
        var dt = new DateTime(ticks, DateTimeKind.Unspecified);
        return new MinamoLocalDateTime((MinamoLocalDateTimeTypeInfo)TypeInfo, dt.AddDays(DateTime.DaysInMonth(dt.Year, dt.Month) - dt.Day).Ticks, Offset);
    }
}

public sealed class MinamoTime : MinamoForeignObject, ITime, IFormattable
{
    private const string DEFAULT_FORMAT = "hh:mm:ss.fffffff";

    private readonly long ticks;

    public MinamoTime(MinamoTimeTypeInfo typeInfo, long ticks) : base(typeInfo) => this.ticks = ticks;

    public long TotalTicks => ticks;

    public int Ticks => (int)(ticks % 10_000_000);

    public int Microseconds => (int)(ticks / DT.TicksPerMicrosecond % 1_000_000);

    public int Milliseconds => (int)(ticks / DT.TicksPerMillisecond % 1000);

    public int Seconds => (int)(ticks / DT.TicksPerSecond % 60);

    public int Minutes => (int)(ticks / DT.TicksPerMinute % 60);

    public int Hours => (int)(ticks / DT.TicksPerHour);

    public override object ToObject() => new TimeOnly(ticks);

    public long ToInteger() => ticks;

    public override MinamoObject Clone() => this;

    public override int GetHashCode() => ticks.GetHashCode();

    public override bool Equals(MinamoObject? other) => other is MinamoTime dt && dt.ticks == ticks;

    public static MinamoTime Parse(MinamoTimeTypeInfo typeInfo, string format, string value)
    {
        var (ticks, _, _) = InputParser.Parse(FormatParser.TimeParser, format, value);
        return new(typeInfo, ticks);
    }

    public string ToString(string? format, IFormatProvider? _ = null)
    {
        var formats = FormatParser.TimeParser.ParseSpecifiers(format ?? DEFAULT_FORMAT);
        var sb = new StringBuilder();

        foreach (var f in formats)
        {
            Formatter.FormatTime(this, sb, f);
        }

        return sb.ToString();
    }

    public override string ToString() => ToString(DEFAULT_FORMAT);
}

public sealed class MinamoTimeDelta : MinamoForeignObject, IInterval, IFormattable
{
    private const string DEFAULT_FORMAT = "+d.HH:mm:ss.fffffff";

    private readonly long ticks;

    public long TotalTicks => ticks;

    public int Ticks => (int)(ticks % 10_000_000);

    public int Microseconds => (int)(ticks / DT.TicksPerMicrosecond % 1_000_000);

    public int Milliseconds => (int)(ticks / DT.TicksPerMillisecond % 1000);

    public int Seconds => (int)(ticks / DT.TicksPerSecond % 60);

    public int Minutes => (int)(ticks / DT.TicksPerMinute % 60);

    public int Hours => (int)(ticks / DT.TicksPerHour % 24);

    public int Days => (int)(ticks / DT.TicksPerDay);

    public MinamoTimeDelta(MinamoTimeDeltaTypeInfo typeInfo, long ticks) : base(typeInfo) => this.ticks = ticks;

    public MinamoTimeDelta(MinamoTimeDeltaTypeInfo typeInfo, TimeSpan timeSpan) : this(typeInfo, timeSpan.Ticks) { }

    public override object ToObject() => ToTimeSpan();

    public long ToInteger() => ticks;

    public TimeSpan ToTimeSpan() => TimeSpan.FromTicks(ticks);

    public MinamoTimeDelta Negate() => new((MinamoTimeDeltaTypeInfo)TypeInfo, -ticks);

    public static MinamoTimeDelta Parse(MinamoTimeDeltaTypeInfo typeInfo, string format, string value)
    {
        var (ticks, _, _) = InputParser.Parse(FormatParser.TimeDeltaParser, format, value);
        return new(typeInfo, ticks);
    }

    public string ToString(string? format, IFormatProvider? _ = null)
    {
        var formats = FormatParser.TimeDeltaParser.ParseSpecifiers(format ?? DEFAULT_FORMAT);
        var sb = new StringBuilder();

        foreach (var f in formats)
        {
            Formatter.FormatInterval(this, sb, f);
        }

        return sb.ToString();
    }

    public override string ToString() => ToString(DEFAULT_FORMAT);

    public override int GetHashCode() => ticks.GetHashCode();

    public override bool Equals(MinamoObject? other) => other is MinamoTimeDelta d && d.ticks == ticks;

    public override MinamoObject Clone() => this;
}

[MinamoType]
public sealed partial class MinamoCalendarTypeInfo : MinamoForeignTypeInfo<TimeModule>
{
    public override string ReflectedTypeName => "Calendar";

    [MinamoStaticMethod]
    internal static int DaysInMonth(int year, int month) => DateTime.DaysInMonth(year, month);

    [MinamoStaticMethod]
    internal static MinamoObject FirstDayOfMonth(MinamoDateTime value) => value.FirstDayOfMonth();

    [MinamoStaticMethod]
    internal static MinamoObject LastDayOfMonth(MinamoDateTime value) => value.LastDayOfMonth();

    [MinamoStaticMethod]
    internal static int DaysInYear(int year) => DateTime.IsLeapYear(year) ? 366 : 365;

    [MinamoStaticMethod]
    internal static bool IsLeapYear(int year) => DateTime.IsLeapYear(year);

    [MinamoStaticMethod]
    internal static MinamoObject ParseDateTime(ExecutionContext ctx, string input, string format)
    {
        var (ticks, offset, hasOffset) =
            InputParser.Parse(FormatParser.LocalDateTimeParser, format, input);

        if (!hasOffset)
        {
            return new MinamoDateTime(ctx.Type<MinamoDateTimeTypeInfo>(), ticks);
        }
        else
        {
            return new MinamoLocalDateTime(ctx.Type<MinamoLocalDateTimeTypeInfo>(), ticks,
                new MinamoTimeDelta(ctx.Type<MinamoTimeDeltaTypeInfo>(), offset));
        }
    }
}

[MinamoType]
public sealed partial class MinamoDateTypeInfo : SpanTypeInfo<MinamoDate>
{
    private const string Date = nameof(Date);

    public MinamoDateTypeInfo() : base(Date) { }

    [MinamoMethod("Add")]
    internal static MinamoObject AddTo(ExecutionContext ctx, MinamoObject self, int years = 0, int months = 0, int days = 0)
    {
        var s = (MinamoDate)self.Clone();

        try
        {
            if (days != 0)
            {
                s.AddDays(days);
            }

            if (months != 0)
            {
                s.AddMonths(months);
            }

            if (years != 0)
            {
                s.AddYears(years);
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            return ctx.Overflow();
        }

        return s;
    }

    [MinamoProperty]
    internal static int Year(MinamoDate self) => self.Year;

    [MinamoProperty]
    internal static int Month(MinamoDate self) => self.Month;

    [MinamoProperty]
    internal static int Day(MinamoDate self) => self.Day;

    [MinamoProperty]
    internal static string DayOfWeek(MinamoDate self) => self.DayOfWeek;

    [MinamoProperty]
    internal static int DayOfYear(MinamoDate self) => self.DayOfYear;

    [MinamoStaticMethod]
    internal static MinamoObject Parse(ExecutionContext ctx, string input, string format)
    {
        try
        {
            return MinamoDate.Parse(ctx.Type<MinamoDateTypeInfo>(), format, input);
        }
        catch (FormatException)
        {
            return ctx.ParsingFailed();
        }
        catch (OverflowException)
        {
            return ctx.Overflow();
        }
    }

    [MinamoStaticMethod(Date)]
    internal static MinamoObject CreateNew(ExecutionContext ctx, int year, int month, int day)
    {
        DateTime dt;

        try
        {
            dt = new DateTime(year, month, day).Date;
        }
        catch (Exception)
        {
            return ctx.Overflow();
        }

        return new MinamoDate(ctx.Type<MinamoDateTypeInfo>(), (int)(dt.Ticks / DT.TicksPerDay));
    }

    [MinamoStaticProperty]
    internal static MinamoDate Default(ExecutionContext ctx) => Min(ctx);

    [MinamoStaticProperty]
    internal static MinamoDate Min(ExecutionContext ctx) => new(ctx.Type<MinamoDateTypeInfo>(), (int)(DateTime.MinValue.Date.Ticks / DT.TicksPerDay));

    [MinamoStaticProperty]
    internal static MinamoDate Max(ExecutionContext ctx) => new(ctx.Type<MinamoDateTypeInfo>(), (int)(DateTime.MaxValue.Date.Ticks / DT.TicksPerDay));
}

[MinamoType]
public sealed partial class MinamoDateTimeTypeInfo : SpanTypeInfo<MinamoDateTime>
{
    private const string DateTimeType = "DateTime";

    public MinamoDateTimeTypeInfo() : base(DateTimeType)
    {
        SetSupportedOperations(Ops.Sub | Ops.Add);
    }

    #region Operations
    protected override MinamoObject SubOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (right is MinamoDateTime dt)
        {
            try
            {
                return new MinamoTimeDelta(DeclaringUnit.TimeDelta, ((MinamoDateTime)left).TotalTicks - dt.TotalTicks);
            }
            catch (Exception)
            {
                return ctx.InvalidValue(right);
            }
        }
        else if (right is MinamoTimeDelta td)
        {
            try
            {
                return new MinamoDateTime(this, ((MinamoDateTime)left).TotalTicks - td.TotalTicks);
            }
            catch (Exception)
            {
                return ctx.InvalidValue(right);
            }
        }

        return ctx.InvalidType(DeclaringUnit.DateTime.TypeId, DeclaringUnit.TimeDelta.TypeId, right);
    }

    protected override MinamoObject AddOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (right is MinamoTimeDelta td)
        {
            try
            {
                return new MinamoDateTime(this, ((MinamoDateTime)left).TotalTicks + td.TotalTicks);
            }
            catch (ArgumentOutOfRangeException)
            {
                return ctx.InvalidValue(right);
            }
        }

        return ctx.InvalidType(DeclaringUnit.TimeDelta.TypeId, right);
    }

    protected override MinamoObject CastOp(ExecutionContext ctx, MinamoObject self, MinamoTypeInfo targetType)
    {
        if (targetType.ReflectedTypeId == DeclaringUnit.Date.ReflectedTypeId)
        {
            return ((MinamoDateTime)self).GetDate(DeclaringUnit.Date);
        }
        else if (targetType.ReflectedTypeId == DeclaringUnit.Time.ReflectedTypeId)
        {
            return ((MinamoDateTime)self).GetTime(DeclaringUnit.Time);
        }

        return base.CastOp(ctx, self, targetType);
    }
    #endregion

    [MinamoMethod("Add")]
    internal static MinamoObject AddTo(ExecutionContext ctx, MinamoObject self, int years = 0, int months = 0, int days = 0,
         double hours = 0, double minutes = 0, double seconds = 0, double milliseconds = 0, long ticks = 0)
    {
        var s = (MinamoDateTime)self.Clone();

        try
        {
            if (ticks != 0)
            {
                s.AddTicks(ticks);
            }

            if (milliseconds != 0)
            {
                s.AddMilliseconds(milliseconds);
            }

            if (seconds != 0)
            {
                s.AddSeconds(seconds);
            }

            if (minutes != 0)
            {
                s.AddMinutes(minutes);
            }

            if (hours != 0)
            {
                s.AddHours(hours);
            }

            if (days != 0)
            {
                s.AddDays(days);
            }

            if (months != 0)
            {
                s.AddMonths(months);
            }

            if (years != 0)
            {
                s.AddYears(years);
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            return ctx.Overflow();
        }

        return s;
    }

    [MinamoProperty]
    internal static int Year(MinamoDateTime self) => self.Year;

    [MinamoProperty]
    internal static int Month(MinamoDateTime self) => self.Month;

    [MinamoProperty]
    internal static int Day(MinamoDateTime self) => self.Day;

    [MinamoProperty]
    internal static string DayOfWeek(MinamoDateTime self) => self.DayOfWeek;

    [MinamoProperty]
    internal static int DayOfYear(MinamoDateTime self) => self.DayOfYear;

    [MinamoProperty]
    internal static int Hour(MinamoDateTime self) => self.Hours;

    [MinamoProperty]
    internal static int Minute(MinamoDateTime self) => self.Minutes;

    [MinamoProperty]
    internal static int Second(MinamoDateTime self) => self.Seconds;

    [MinamoProperty]
    internal static int Millisecond(MinamoDateTime self) => self.Milliseconds;

    [MinamoProperty]
    internal static int Tick(MinamoDateTime self) => self.Ticks;

    [MinamoProperty]
    internal static long TotalTicks(MinamoDateTime self) => self.TotalTicks;

    [MinamoProperty]
    internal static MinamoObject Date(ExecutionContext ctx, MinamoDateTime self) => new MinamoDate(ctx.Type<MinamoDateTypeInfo>(), new DateTime(self.TotalTicks));

    [MinamoProperty]
    internal static MinamoObject Time(ExecutionContext ctx, MinamoDateTime self) => new MinamoTime(ctx.Type<MinamoTimeTypeInfo>(), TimeOnly.FromDateTime(new DateTime(self.TotalTicks)).Ticks);

    [MinamoStaticMethod]
    internal static MinamoObject Parse(ExecutionContext ctx, string input, string format)
    {
        try
        {
            return MinamoDateTime.Parse(ctx.Type<MinamoDateTimeTypeInfo>(), format, input);
        }
        catch (FormatException)
        {
            return ctx.ParsingFailed();
        }
        catch (OverflowException)
        {
            return ctx.Overflow();
        }
    }

    [MinamoStaticMethod(DateTimeType)]
    internal static MinamoObject CreateNew(ExecutionContext ctx, int year, int month, int day,
        int hour = 0, int minute = 0, int second = 0, int millisecond = 0)
    {
        var dt = new DateTime(year, month, day, hour, minute, second, millisecond, DateTimeKind.Utc);
        return new MinamoDateTime(ctx.Type<MinamoDateTimeTypeInfo>(), dt.Ticks);
    }

    [MinamoStaticMethod]
    internal static MinamoObject FromTicks(ExecutionContext ctx, long ticks) =>
        new MinamoDateTime(ctx.Type<MinamoDateTimeTypeInfo>(), ticks);

    [MinamoStaticProperty]
    internal static MinamoDateTime Default(ExecutionContext ctx) => Min(ctx);

    [MinamoStaticProperty]
    internal static MinamoDateTime Min(ExecutionContext ctx) => new(ctx.Type<MinamoDateTimeTypeInfo>(), DateTime.MinValue.Ticks);

    [MinamoStaticProperty]
    internal static MinamoDateTime Max(ExecutionContext ctx) => new(ctx.Type<MinamoDateTimeTypeInfo>(), DateTime.MaxValue.Ticks);

    [MinamoStaticMethod]
    internal static MinamoDateTime UtcNow(ExecutionContext ctx) =>
        new(ctx.Type<MinamoDateTimeTypeInfo>(), DateTime.UtcNow.Ticks);

    [MinamoMethod]
    internal static MinamoObject ToLocal(
        ExecutionContext ctx,
        MinamoDateTime self,
        MinamoTimeDelta? offset = null) =>
        MinamoLocalDateTimeTypeInfo.FromUtc(ctx, self, offset);
}

[MinamoType]
public sealed partial class MinamoLocalDateTimeTypeInfo : SpanTypeInfo<MinamoDateTime>
{
    private const string LocalDateTime = nameof(LocalDateTime);

    public MinamoTimeDeltaTypeInfo TypeDeltaTypeInfo => DeclaringUnit.TimeDelta;

    public MinamoLocalDateTimeTypeInfo() : base(LocalDateTime) { }

    #region Operations
    private static long GetUtcTicks(MinamoObject value) => ((MinamoLocalDateTime)value).UtcTicks;

    protected override MinamoObject EqOp(ExecutionContext ctx, MinamoObject left, MinamoObject right) =>
        right.TypeId == left.TypeId && GetUtcTicks(left) == GetUtcTicks(right) ? True : False;

    protected override MinamoObject NeqOp(ExecutionContext ctx, MinamoObject left, MinamoObject right) =>
        right.TypeId != left.TypeId || GetUtcTicks(left) != GetUtcTicks(right) ? True : False;

    protected override MinamoObject GtOp(ExecutionContext ctx, MinamoObject left, MinamoObject right) =>
        right.TypeId != left.TypeId ? ctx.InvalidType(left.TypeId, right) :
        GetUtcTicks(left) > GetUtcTicks(right) ? True : False;

    protected override MinamoObject LtOp(ExecutionContext ctx, MinamoObject left, MinamoObject right) =>
        right.TypeId != left.TypeId ? ctx.InvalidType(left.TypeId, right) :
        GetUtcTicks(left) < GetUtcTicks(right) ? True : False;

    protected override MinamoObject GteOp(ExecutionContext ctx, MinamoObject left, MinamoObject right) =>
        right.TypeId != left.TypeId ? ctx.InvalidType(left.TypeId, right) :
        GetUtcTicks(left) >= GetUtcTicks(right) ? True : False;

    protected override MinamoObject LteOp(ExecutionContext ctx, MinamoObject left, MinamoObject right) =>
        right.TypeId != left.TypeId ? ctx.InvalidType(left.TypeId, right) :
        GetUtcTicks(left) <= GetUtcTicks(right) ? True : False;

    protected override MinamoObject SubOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        var self = (MinamoLocalDateTime)left;

        if (right is MinamoLocalDateTime dt)
        {
            try
            {
                return new MinamoTimeDelta(DeclaringUnit.TimeDelta, self.UtcTicks - dt.UtcTicks);
            }
            catch (Exception)
            {
                return ctx.InvalidValue(right);
            }
        }
        else if (right is MinamoTimeDelta td)
        {
            try
            {
                var ticks = checked(self.TotalTicks - td.TotalTicks);
                return ticks < 0 || ticks > DateTime.MaxValue.Ticks
                    ? ctx.Overflow()
                    : new MinamoLocalDateTime(this, ticks, self.Offset);
            }
            catch (OverflowException)
            {
                return ctx.Overflow();
            }
        }

        return ctx.InvalidType(DeclaringUnit.LocalDateTime.TypeId, DeclaringUnit.TimeDelta.TypeId, right);
    }

    protected override MinamoObject AddOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        var self = (MinamoLocalDateTime)left;

        if (right is MinamoTimeDelta td)
        {
            try
            {
                var ticks = checked(self.TotalTicks + td.TotalTicks);
                return ticks < 0 || ticks > DateTime.MaxValue.Ticks
                    ? ctx.Overflow()
                    : new MinamoLocalDateTime(this, ticks, self.Offset);
            }
            catch (OverflowException)
            {
                return ctx.Overflow();
            }
        }

        return ctx.InvalidType(DeclaringUnit.TimeDelta.TypeId, right);
    }

    protected override MinamoObject CastOp(ExecutionContext ctx, MinamoObject self, MinamoTypeInfo targetType)
    {
        if (targetType.ReflectedTypeId == DeclaringUnit.Date.ReflectedTypeId)
        {
            return ((MinamoLocalDateTime)self).GetDate(DeclaringUnit.Date);
        }
        else if (targetType.ReflectedTypeId == DeclaringUnit.Time.ReflectedTypeId)
        {
            return ((MinamoLocalDateTime)self).GetTime(DeclaringUnit.Time);
        }

        return base.CastOp(ctx, self, targetType);
    }
    #endregion

    [MinamoMethod("Add")]
    internal static MinamoObject AddTo(ExecutionContext ctx, MinamoObject self, int years = 0, int months = 0, int days = 0,
    double hours = 0, double minutes = 0, double seconds = 0, double milliseconds = 0, long ticks = 0)
    {
        var s = (MinamoLocalDateTime)self.Clone();

        try
        {
            if (ticks != 0)
            {
                s.AddTicks(ticks);
            }

            if (milliseconds != 0)
            {
                s.AddMilliseconds(milliseconds);
            }

            if (seconds != 0)
            {
                s.AddSeconds(seconds);
            }

            if (minutes != 0)
            {
                s.AddMinutes(minutes);
            }

            if (hours != 0)
            {
                s.AddHours(hours);
            }

            if (days != 0)
            {
                s.AddDays(days);
            }

            if (months != 0)
            {
                s.AddMonths(months);
            }

            if (years != 0)
            {
                s.AddYears(years);
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            return ctx.Overflow();
        }

        return s;
    }

    [MinamoProperty]
    internal static int Year(MinamoLocalDateTime self) => self.Year;

    [MinamoProperty]
    internal static int Month(MinamoLocalDateTime self) => self.Month;

    [MinamoProperty]
    internal static int Day(MinamoLocalDateTime self) => self.Day;

    [MinamoProperty]
    internal static string DayOfWeek(MinamoLocalDateTime self) => self.DayOfWeek;

    [MinamoProperty]
    internal static int DayOfYear(MinamoLocalDateTime self) => self.DayOfYear;

    [MinamoProperty]
    internal static int Hour(MinamoLocalDateTime self) => self.Hours;

    [MinamoProperty]
    internal static int Minute(MinamoLocalDateTime self) => self.Minutes;

    [MinamoProperty]
    internal static int Second(MinamoLocalDateTime self) => self.Seconds;

    [MinamoProperty]
    internal static int Millisecond(MinamoLocalDateTime self) => self.Milliseconds;

    [MinamoProperty]
    internal static int Tick(MinamoLocalDateTime self) => self.Ticks;

    [MinamoProperty]
    internal static long TotalTicks(MinamoLocalDateTime self) => self.TotalTicks;

    [MinamoProperty]
    internal static long UtcTicks(MinamoLocalDateTime self) => self.UtcTicks;

    [MinamoProperty]
    internal static MinamoObject Date(ExecutionContext ctx, MinamoDateTime self) => new MinamoDate(ctx.Type<MinamoDateTypeInfo>(), new DateTime(self.TotalTicks));

    [MinamoProperty]
    internal static MinamoObject Time(ExecutionContext ctx, MinamoDateTime self) => new MinamoTime(ctx.Type<MinamoTimeTypeInfo>(), TimeOnly.FromDateTime(new DateTime(self.TotalTicks)).Ticks);

    [MinamoProperty]
    internal static MinamoObject Offset(MinamoLocalDateTime self) => self.Offset;

    private static MinamoTimeDelta GetOffset(
        ExecutionContext ctx,
        MinamoTimeDelta? offset,
        DateTime localDateTime)
    {
        if (offset is null)
        {
            return new MinamoTimeDelta(
                ctx.Type<MinamoTimeDeltaTypeInfo>(),
                TimeZoneInfo.Local.GetUtcOffset(localDateTime).Ticks);
        }
        else
        {
            return offset;
        }
    }

    private static bool ValidateOffset(ExecutionContext ctx, MinamoTimeDelta offset)
    {
        var ticks = offset.TotalTicks;
        if (ticks < -14 * DT.TicksPerHour
            || ticks > 14 * DT.TicksPerHour
            || ticks % DT.TicksPerMinute != 0)
        {
            ctx.InvalidValue(offset);
            return false;
        }
        return true;
    }

    [MinamoStaticMethod]
    internal static MinamoObject Parse(ExecutionContext ctx, string input, string format)
    {
        try
        {
            var result = (MinamoLocalDateTime)MinamoLocalDateTime.Parse(
                ctx.Type<MinamoLocalDateTimeTypeInfo>(), format, input);
            return ValidateOffset(ctx, result.Offset) ? result : Nil;
        }
        catch (FormatException)
        {
            return ctx.ParsingFailed();
        }
        catch (OverflowException)
        {
            return ctx.Overflow();
        }
    }

    [MinamoStaticMethod(LocalDateTime)]
    internal static MinamoObject CreateNew(ExecutionContext ctx, int year, int month, int day,
        int hour = 0, int minute = 0, int second = 0, int millisecond = 0, MinamoTimeDelta? offset = null)
    {
        var dt = new DateTime(year, month, day, hour, minute, second, millisecond, DateTimeKind.Unspecified);
        var delta = GetOffset(ctx, offset, dt);
        return ValidateOffset(ctx, delta)
            ? new MinamoLocalDateTime(ctx.Type<MinamoLocalDateTimeTypeInfo>(), dt.Ticks, delta)
            : Nil;
    }

    [MinamoStaticMethod]
    internal static MinamoObject FromTicks(ExecutionContext ctx, long ticks, MinamoTimeDelta? offset = null)
    {
        try
        {
            var delta = GetOffset(ctx, offset, new DateTime(ticks, DateTimeKind.Unspecified));
            return ValidateOffset(ctx, delta)
                ? new MinamoLocalDateTime(ctx.Type<MinamoLocalDateTimeTypeInfo>(), ticks, delta)
                : Nil;
        }
        catch (ArgumentOutOfRangeException)
        {
            return ctx.InvalidValue(ticks);
        }
    }

    [MinamoStaticMethod]
    internal static MinamoObject FromUtc(ExecutionContext ctx, MinamoDateTime dateTime, MinamoTimeDelta? offset = null)
    {
        var utc = dateTime.ToDateTime();
        offset ??= new MinamoTimeDelta(
            ctx.Type<MinamoTimeDeltaTypeInfo>(),
            TimeZoneInfo.Local.GetUtcOffset(utc));

        if (!ValidateOffset(ctx, offset))
        {
            return Nil;
        }

        try
        {
            var localTicks = checked(dateTime.TotalTicks + offset.TotalTicks);
            if (localTicks < 0 || localTicks > DateTime.MaxValue.Ticks)
            {
                return ctx.Overflow();
            }
            return new MinamoLocalDateTime(
                ctx.Type<MinamoLocalDateTimeTypeInfo>(),
                localTicks,
                offset);
        }
        catch (OverflowException)
        {
            return ctx.Overflow();
        }
    }

    [MinamoMethod]
    internal static MinamoObject ToUtc(ExecutionContext ctx, MinamoLocalDateTime self)
    {
        var utcTicks = self.UtcTicks;
        return utcTicks < 0 || utcTicks > DateTime.MaxValue.Ticks
            ? ctx.Overflow()
            : new MinamoDateTime(ctx.Type<MinamoDateTimeTypeInfo>(), utcTicks);
    }

    [MinamoStaticMethod]
    internal static MinamoLocalDateTime Now(ExecutionContext ctx) =>
        CreateNow(ctx);

    [MinamoStaticProperty]
    internal static MinamoTimeDelta SystemOffset(ExecutionContext ctx) =>
        new(ctx.Type<MinamoTimeDeltaTypeInfo>(), DateTimeOffset.Now.Offset);

    [MinamoStaticProperty]
    internal static MinamoLocalDateTime Default(ExecutionContext ctx) => Min(ctx);

    [MinamoStaticProperty]
    internal static MinamoLocalDateTime Min(ExecutionContext ctx) =>
        new(ctx.Type<MinamoLocalDateTimeTypeInfo>(), DateTime.MinValue.Ticks,
            new MinamoTimeDelta(ctx.Type<MinamoTimeDeltaTypeInfo>(), TimeSpan.Zero));

    [MinamoStaticProperty]
    internal static MinamoLocalDateTime Max(ExecutionContext ctx) =>
        new(ctx.Type<MinamoLocalDateTimeTypeInfo>(), DateTime.MaxValue.Ticks,
            new MinamoTimeDelta(ctx.Type<MinamoTimeDeltaTypeInfo>(), TimeSpan.Zero));

    private static MinamoLocalDateTime CreateNow(ExecutionContext ctx)
    {
        var now = DateTimeOffset.Now;
        return new(
            ctx.Type<MinamoLocalDateTimeTypeInfo>(),
            now.DateTime.Ticks,
            new MinamoTimeDelta(ctx.Type<MinamoTimeDeltaTypeInfo>(), now.Offset));
    }
}

[MinamoType]
public sealed partial class MinamoTimeTypeInfo : SpanTypeInfo<MinamoTime>
{
    private const string Time = nameof(Time);

    public MinamoTimeTypeInfo() : base(Time) { }

    [MinamoProperty]
    internal static int Hour(MinamoTime self) => self.Hours;

    [MinamoProperty]
    internal static int Minute(MinamoTime self) => self.Minutes;

    [MinamoProperty]
    internal static int Second(MinamoTime self) => self.Seconds;

    [MinamoProperty]
    internal static int Millisecond(MinamoTime self) => self.Milliseconds;

    [MinamoProperty]
    internal static int Tick(MinamoTime self) => self.Ticks;

    [MinamoProperty]
    internal static long TotalTicks(MinamoTime self) => self.TotalTicks;

    [MinamoStaticMethod(Time)]
    internal static MinamoObject CreateNew(ExecutionContext ctx, int hour = 0, int minute = 0, int second = 0, int millisecond = 0, int tick = 0)
    {
        var ticks = tick + DT.Sum(0, hour, minute, second, millisecond);
        return new MinamoTime(ctx.Type<MinamoTimeTypeInfo>(), ticks);
    }

    [MinamoStaticMethod]
    internal static MinamoObject FromTicks(ExecutionContext ctx, long ticks) => new MinamoTime(ctx.Type<MinamoTimeTypeInfo>(), ticks);

    [MinamoStaticMethod]
    internal static MinamoObject Parse(ExecutionContext ctx, string input, string format)
    {
        try
        {
            return MinamoTime.Parse(ctx.Type<MinamoTimeTypeInfo>(), format, input);
        }
        catch (FormatException)
        {
            return ctx.ParsingFailed();
        }
        catch (OverflowException)
        {
            return ctx.Overflow();
        }
    }

    [MinamoStaticProperty]
    internal static MinamoTime Default(ExecutionContext ctx) => Min(ctx);

    [MinamoStaticProperty]
    internal static MinamoTime Min(ExecutionContext ctx) => new(ctx.Type<MinamoTimeTypeInfo>(), DateTime.MinValue.TimeOfDay.Ticks);

    [MinamoStaticProperty]
    internal static MinamoTime Max(ExecutionContext ctx) => new(ctx.Type<MinamoTimeTypeInfo>(), DateTime.MaxValue.TimeOfDay.Ticks);
}

[MinamoType]
public sealed partial class MinamoTimeDeltaTypeInfo : SpanTypeInfo<MinamoTimeDelta>
{
    private const string TimeDelta = nameof(TimeDelta);

    public MinamoTimeDeltaTypeInfo() : base(TimeDelta)
    {
        SetSupportedOperations(Ops.Add | Ops.Sub | Ops.Neg);
    }

    #region Operations
    protected override MinamoObject NegOp(ExecutionContext ctx, MinamoObject arg) => ((MinamoTimeDelta)arg).Negate();

    protected override MinamoObject AddOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (right.TypeId != left.TypeId)
        {
            return ctx.InvalidType(left.TypeId, right);
        }

        return new MinamoTimeDelta(this, ((MinamoTimeDelta)left).TotalTicks + ((MinamoTimeDelta)right).TotalTicks);
    }

    protected override MinamoObject SubOp(ExecutionContext ctx, MinamoObject left, MinamoObject right)
    {
        if (right.TypeId != left.TypeId)
        {
            return ctx.InvalidType(left.TypeId, right);
        }

        try
        {
            return new MinamoTimeDelta(this, ((MinamoTimeDelta)left).TotalTicks - ((MinamoTimeDelta)right).TotalTicks);
        }
        catch (OverflowException)
        {
            return ctx.Overflow();
        }
    }
    #endregion

    [MinamoProperty]
    internal static int Days(MinamoTimeDelta self) => self.Days;

    [MinamoProperty]
    internal static int Hours(MinamoTimeDelta self) => self.Hours;

    [MinamoProperty]
    internal static int Minutes(MinamoTimeDelta self) => self.Minutes;

    [MinamoProperty]
    internal static int Seconds(MinamoTimeDelta self) => self.Seconds;

    [MinamoProperty]
    internal static int Milliseconds(MinamoTimeDelta self) => self.Milliseconds;

    [MinamoProperty]
    internal static int Ticks(MinamoTimeDelta self) => self.Ticks;

    [MinamoProperty]
    internal static long TotalTicks(MinamoTimeDelta self) => self.TotalTicks;

    [MinamoMethod]
    internal static MinamoObject Negate(MinamoTimeDelta self) => self.Negate();

    [MinamoStaticMethod]
    internal static MinamoObject FromTicks(ExecutionContext ctx, long ticks) =>
        new MinamoTimeDelta(ctx.Type<MinamoTimeDeltaTypeInfo>(), ticks);

    [MinamoStaticMethod]
    internal static MinamoObject Parse(ExecutionContext ctx, string input, string format)
    {
        try
        {
            return MinamoTimeDelta.Parse(ctx.Type<MinamoTimeDeltaTypeInfo>(), format, input);
        }
        catch (FormatException)
        {
            return ctx.ParsingFailed();
        }
        catch (OverflowException)
        {
            return ctx.Overflow();
        }
    }

    [MinamoStaticMethod(TimeDelta)]
    internal static MinamoObject New(ExecutionContext ctx, int days = 0, int hours = 0, int minutes = 0,
        int seconds = 0, int milliseconds = 0, long ticks = 0)
    {
        ticks += DT.Sum(days, hours, minutes, seconds, milliseconds);
        return new MinamoTimeDelta(ctx.Type<MinamoTimeDeltaTypeInfo>(), ticks);
    }

    [MinamoStaticProperty]
    internal static MinamoTimeDelta Default(ExecutionContext ctx) => new(ctx.Type<MinamoTimeDeltaTypeInfo>(), TimeSpan.Zero.Ticks);

    [MinamoStaticProperty]
    internal static MinamoTimeDelta Min(ExecutionContext ctx) => new(ctx.Type<MinamoTimeDeltaTypeInfo>(), TimeSpan.MinValue.Ticks);

    [MinamoStaticProperty]
    internal static MinamoTimeDelta Max(ExecutionContext ctx) => new(ctx.Type<MinamoTimeDeltaTypeInfo>(), TimeSpan.MaxValue.Ticks);
}
