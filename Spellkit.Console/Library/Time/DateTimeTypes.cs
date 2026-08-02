using Spellkit.Codegen;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System;
using System.Text;

namespace Spellkit.Library.Time;

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
    SpellkitObject GetDate(SpellkitDateTypeInfo typeInfo);

    SpellkitObject GetTime(SpellkitTimeTypeInfo typeInfo);

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

public abstract class SpanTypeInfo<T> : SpellkitForeignTypeInfo<TimeModule>
    where T : SpellkitObject, ISpan, IFormattable
{
    public override string ReflectedTypeName { get; }

    protected SpanTypeInfo(string typeName)
    {
        ReflectedTypeName = typeName;
        AddMixins(SpellkitTypeCodes.Order, SpellkitTypeCodes.Equatable);
    }

    #region Operations
    protected override SpellkitObject ToStringOp(ExecutionContext ctx, SpellkitObject arg, SpellkitObject format)
    {
        if (format.Is(SpellkitTypeCodes.Nil))
        {
            return new SpellkitString(arg.ToString());
        }

        if (format.TypeId is not SpellkitTypeCodes.String and not SpellkitTypeCodes.Char)
        {
            return Nil;
        }

        try
        {
            return new SpellkitString(((T)arg).ToString(format.ToString(), null));
        }
        catch (FormatException)
        {
            return ctx.ParsingFailed();
        }
    }

    protected override SpellkitObject EqOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (right.TypeId != left.TypeId)
        {
            return SpellkitBool.False;
        }

        return ((T)left).TotalTicks == ((T)right).TotalTicks ? True : False;
    }

    protected override SpellkitObject NeqOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (right.TypeId != left.TypeId)
        {
            return SpellkitBool.True;
        }

        return ((T)left).TotalTicks != ((T)right).TotalTicks ? True : False;
    }

    protected override SpellkitObject GtOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (right.TypeId != left.TypeId)
        {
            return ctx.InvalidType(left.TypeId, right);
        }

        return ((T)left).TotalTicks > ((T)right).TotalTicks ? True : False;
    }

    protected override SpellkitObject LtOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (right.TypeId != left.TypeId)
        {
            return ctx.InvalidType(left.TypeId, right);
        }

        return ((T)left).TotalTicks < ((T)right).TotalTicks ? True : False;
    }

    protected override SpellkitObject GteOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (right.TypeId != left.TypeId)
        {
            return ctx.InvalidType(left.TypeId, right);
        }

        return ((T)left).TotalTicks >= ((T)right).TotalTicks ? True : False;
    }

    protected override SpellkitObject LteOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (right.TypeId != left.TypeId)
        {
            return ctx.InvalidType(left.TypeId, right);
        }

        return ((T)left).TotalTicks <= ((T)right).TotalTicks ? True : False;
    }

    protected override SpellkitObject CastOp(ExecutionContext ctx, SpellkitObject self, SpellkitTypeInfo targetType) =>
       targetType.ReflectedTypeId switch
       {
           SpellkitTypeCodes.Integer => SpellkitInteger.Get(((T)self).ToInteger()),
           _ => base.CastOp(ctx, self, targetType)
       };
    #endregion
}

public sealed class SpellkitDate : SpellkitForeignObject, IDate, IFormattable
{
    private const string DEFAULT_FORMAT = "yyyy-MM-dd";

    private int days;

    public SpellkitDate(SpellkitDateTypeInfo typeInfo, int days) : base(typeInfo) => this.days = days;

    public SpellkitDate(SpellkitDateTypeInfo typeInfo, DateTime dateTime) : this(typeInfo, DateOnly.FromDateTime(dateTime).DayNumber) { }

    public long TotalTicks => days * DT.TicksPerDay;

    public int Year => new DateTime(TotalTicks).Year;

    public int Month => new DateTime(TotalTicks).Month;

    public int Day => new DateTime(TotalTicks).Day;

    public string DayOfWeek => new DateTime(TotalTicks).DayOfWeek.ToString();

    public int DayOfYear => new DateTime(TotalTicks).DayOfYear;

    public override object ToObject() => new DateOnly(Year, Month, Day);

    public long ToInteger() => days;

    public override SpellkitObject Clone() => new SpellkitDate((SpellkitDateTypeInfo)TypeInfo, days);

    public override int GetHashCode() => days.GetHashCode();

    public override bool Equals(SpellkitObject? other) => other is SpellkitDate dt && dt.days == days;

    public void AddDays(int days) => SetDays(new DateTime(TotalTicks).AddDays(days).Date);

    public void AddMonths(int months) => SetDays(new DateTime(TotalTicks).AddMonths(months).Date);

    public void AddYears(int years) => SetDays(new DateTime(TotalTicks).AddYears(years).Date);

    public static SpellkitDate Parse(SpellkitDateTypeInfo typeInfo, string format, string value)
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

public class SpellkitDateTime : SpellkitForeignObject, IDateTime, IFormattable
{
    private const string FORMAT = "yyyy-MM-dd HH:mm:ss.fffffff";

    protected long ticks;

    internal SpellkitDateTime(SpanTypeInfo<SpellkitDateTime> typeInfo, long ticks) : base(typeInfo) =>
        this.ticks = ticks;

    public DateTime ToDateTime() => new(ticks, DateTimeKind.Utc);

    public override object ToObject() => ToDateTime();

    public override SpellkitObject Clone() => new SpellkitDateTime((SpanTypeInfo<SpellkitDateTime>)TypeInfo, ticks);

    public override bool Equals(SpellkitObject? other) => other is SpellkitDateTime dt && dt.ticks == ticks;

    public override int GetHashCode() => ticks.GetHashCode();

    public override string ToString() => ToString(FORMAT);

    public long ToInteger() => ticks;

    public static SpellkitDateTime Parse(SpellkitForeignTypeInfo typeInfo, string format, string value)
    {
        var (ticks, _, _) = InputParser.Parse(FormatParser.DateTimeParser, format, value);
        return new((SpanTypeInfo<SpellkitDateTime>)typeInfo, ticks);
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

    public virtual SpellkitDateTime FirstDayOfMonth()
    {
        var dt = new DateTime(ticks, DateTimeKind.Unspecified);
        return new SpellkitDateTime((SpanTypeInfo<SpellkitDateTime>)TypeInfo, dt.AddDays(-dt.Day + 1).Ticks);
    }

    public virtual SpellkitDateTime LastDayOfMonth()
    {
        var dt = new DateTime(ticks, DateTimeKind.Unspecified);
        return new SpellkitDateTime((SpanTypeInfo<SpellkitDateTime>)TypeInfo, dt.AddDays(DateTime.DaysInMonth(dt.Year, dt.Month) - dt.Day).Ticks);
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

    public SpellkitObject GetDate(SpellkitDateTypeInfo typeInfo) =>
        new SpellkitDate(typeInfo, DateOnly.FromDateTime(new DateTime(ticks)).DayNumber);

    public SpellkitObject GetTime(SpellkitTimeTypeInfo typeInfo) =>
        new SpellkitTime(typeInfo, TimeOnly.FromDateTime(new DateTime(ticks)).Ticks);

    private void SetTicks(DateTime dt) => ticks = dt.Ticks;
    #endregion
}

public sealed class SpellkitLocalDateTime : SpellkitDateTime, ILocalDateTime
{
    private const string FORMAT = "yyyy-MM-dd HH:mm:ss.fffffffzzz";

    public SpellkitTimeDelta Offset { get; }

    IInterval ILocalDateTime.Interval => Offset;

    internal SpellkitLocalDateTime(SpellkitLocalDateTimeTypeInfo typeInfo, long ticks, SpellkitTimeDelta offset)
        : base(typeInfo, ticks) => this.Offset = offset;

    public long UtcTicks => ticks - Offset.TotalTicks;

    public override bool Equals(SpellkitObject? other) =>
        other is SpellkitLocalDateTime dt && dt.UtcTicks == UtcTicks;

    public static new SpellkitDateTime Parse(SpellkitForeignTypeInfo typeInfo, string format, string value)
    {
        var ti = (SpellkitLocalDateTimeTypeInfo)typeInfo;
        var (ticks, offset, hasOffset) =
            InputParser.Parse(FormatParser.LocalDateTimeParser, format, value);
        var resolvedOffset = hasOffset
            ? TimeSpan.FromTicks(offset)
            : TimeZoneInfo.Local.GetUtcOffset(new DateTime(ticks, DateTimeKind.Unspecified));
        return new SpellkitLocalDateTime(ti, ticks,
            new SpellkitTimeDelta(ti.TypeDeltaTypeInfo, resolvedOffset));
    }

    public override SpellkitObject Clone() =>
        new SpellkitLocalDateTime((SpellkitLocalDateTimeTypeInfo)TypeInfo, ticks, Offset);

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

    public override SpellkitDateTime FirstDayOfMonth()
    {
        var dt = new DateTime(ticks, DateTimeKind.Unspecified);
        return new SpellkitLocalDateTime((SpellkitLocalDateTimeTypeInfo)TypeInfo, dt.AddDays(-dt.Day + 1).Ticks, Offset);
    }

    public override SpellkitDateTime LastDayOfMonth()
    {
        var dt = new DateTime(ticks, DateTimeKind.Unspecified);
        return new SpellkitLocalDateTime((SpellkitLocalDateTimeTypeInfo)TypeInfo, dt.AddDays(DateTime.DaysInMonth(dt.Year, dt.Month) - dt.Day).Ticks, Offset);
    }
}

public sealed class SpellkitTime : SpellkitForeignObject, ITime, IFormattable
{
    private const string DEFAULT_FORMAT = "hh:mm:ss.fffffff";

    private readonly long ticks;

    public SpellkitTime(SpellkitTimeTypeInfo typeInfo, long ticks) : base(typeInfo) => this.ticks = ticks;

    public long TotalTicks => ticks;

    public int Ticks => (int)(ticks % 10_000_000);

    public int Microseconds => (int)(ticks / DT.TicksPerMicrosecond % 1_000_000);

    public int Milliseconds => (int)(ticks / DT.TicksPerMillisecond % 1000);

    public int Seconds => (int)(ticks / DT.TicksPerSecond % 60);

    public int Minutes => (int)(ticks / DT.TicksPerMinute % 60);

    public int Hours => (int)(ticks / DT.TicksPerHour);

    public override object ToObject() => new TimeOnly(ticks);

    public long ToInteger() => ticks;

    public override SpellkitObject Clone() => this;

    public override int GetHashCode() => ticks.GetHashCode();

    public override bool Equals(SpellkitObject? other) => other is SpellkitTime dt && dt.ticks == ticks;

    public static SpellkitTime Parse(SpellkitTimeTypeInfo typeInfo, string format, string value)
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

public sealed class SpellkitTimeDelta : SpellkitForeignObject, IInterval, IFormattable
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

    public SpellkitTimeDelta(SpellkitTimeDeltaTypeInfo typeInfo, long ticks) : base(typeInfo) => this.ticks = ticks;

    public SpellkitTimeDelta(SpellkitTimeDeltaTypeInfo typeInfo, TimeSpan timeSpan) : this(typeInfo, timeSpan.Ticks) { }

    public override object ToObject() => ToTimeSpan();

    public long ToInteger() => ticks;

    public TimeSpan ToTimeSpan() => TimeSpan.FromTicks(ticks);

    public SpellkitTimeDelta Negate() => new((SpellkitTimeDeltaTypeInfo)TypeInfo, -ticks);

    public static SpellkitTimeDelta Parse(SpellkitTimeDeltaTypeInfo typeInfo, string format, string value)
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

    public override bool Equals(SpellkitObject? other) => other is SpellkitTimeDelta d && d.ticks == ticks;

    public override SpellkitObject Clone() => this;
}

[SpellkitType]
public sealed partial class SpellkitCalendarTypeInfo : SpellkitForeignTypeInfo<TimeModule>
{
    public override string ReflectedTypeName => "Calendar";

    [SpellkitStaticMethod]
    internal static int DaysInMonth(int year, int month) => DateTime.DaysInMonth(year, month);

    [SpellkitStaticMethod]
    internal static SpellkitObject FirstDayOfMonth(SpellkitDateTime value) => value.FirstDayOfMonth();

    [SpellkitStaticMethod]
    internal static SpellkitObject LastDayOfMonth(SpellkitDateTime value) => value.LastDayOfMonth();

    [SpellkitStaticMethod]
    internal static int DaysInYear(int year) => DateTime.IsLeapYear(year) ? 366 : 365;

    [SpellkitStaticMethod]
    internal static bool IsLeapYear(int year) => DateTime.IsLeapYear(year);

    [SpellkitStaticMethod]
    internal static SpellkitObject ParseDateTime(ExecutionContext ctx, string input, string format)
    {
        var (ticks, offset, hasOffset) =
            InputParser.Parse(FormatParser.LocalDateTimeParser, format, input);

        if (!hasOffset)
        {
            return new SpellkitDateTime(ctx.Type<SpellkitDateTimeTypeInfo>(), ticks);
        }
        else
        {
            return new SpellkitLocalDateTime(ctx.Type<SpellkitLocalDateTimeTypeInfo>(), ticks,
                new SpellkitTimeDelta(ctx.Type<SpellkitTimeDeltaTypeInfo>(), offset));
        }
    }
}

[SpellkitType]
public sealed partial class SpellkitDateTypeInfo : SpanTypeInfo<SpellkitDate>
{
    private const string Date = nameof(Date);

    public SpellkitDateTypeInfo() : base(Date) { }

    [SpellkitMethod("Add")]
    internal static SpellkitObject AddTo(ExecutionContext ctx, SpellkitObject self, int years = 0, int months = 0, int days = 0)
    {
        var s = (SpellkitDate)self.Clone();

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

    [SpellkitProperty]
    internal static int Year(SpellkitDate self) => self.Year;

    [SpellkitProperty]
    internal static int Month(SpellkitDate self) => self.Month;

    [SpellkitProperty]
    internal static int Day(SpellkitDate self) => self.Day;

    [SpellkitProperty]
    internal static string DayOfWeek(SpellkitDate self) => self.DayOfWeek;

    [SpellkitProperty]
    internal static int DayOfYear(SpellkitDate self) => self.DayOfYear;

    [SpellkitStaticMethod]
    internal static SpellkitObject Parse(ExecutionContext ctx, string input, string format)
    {
        try
        {
            return SpellkitDate.Parse(ctx.Type<SpellkitDateTypeInfo>(), format, input);
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

    [SpellkitStaticMethod(Date)]
    internal static SpellkitObject CreateNew(ExecutionContext ctx, int year, int month, int day)
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

        return new SpellkitDate(ctx.Type<SpellkitDateTypeInfo>(), (int)(dt.Ticks / DT.TicksPerDay));
    }

    [SpellkitStaticProperty]
    internal static SpellkitDate Default(ExecutionContext ctx) => Min(ctx);

    [SpellkitStaticProperty]
    internal static SpellkitDate Min(ExecutionContext ctx) => new(ctx.Type<SpellkitDateTypeInfo>(), (int)(DateTime.MinValue.Date.Ticks / DT.TicksPerDay));

    [SpellkitStaticProperty]
    internal static SpellkitDate Max(ExecutionContext ctx) => new(ctx.Type<SpellkitDateTypeInfo>(), (int)(DateTime.MaxValue.Date.Ticks / DT.TicksPerDay));
}

[SpellkitType]
public sealed partial class SpellkitDateTimeTypeInfo : SpanTypeInfo<SpellkitDateTime>
{
    private const string DateTimeType = "DateTime";

    public SpellkitDateTimeTypeInfo() : base(DateTimeType)
    {
        SetSupportedOperations(Ops.Sub | Ops.Add);
    }

    #region Operations
    protected override SpellkitObject SubOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (right is SpellkitDateTime dt)
        {
            try
            {
                return new SpellkitTimeDelta(DeclaringUnit.TimeDelta, ((SpellkitDateTime)left).TotalTicks - dt.TotalTicks);
            }
            catch (Exception)
            {
                return ctx.InvalidValue(right);
            }
        }
        else if (right is SpellkitTimeDelta td)
        {
            try
            {
                return new SpellkitDateTime(this, ((SpellkitDateTime)left).TotalTicks - td.TotalTicks);
            }
            catch (Exception)
            {
                return ctx.InvalidValue(right);
            }
        }

        return ctx.InvalidType(DeclaringUnit.DateTime.TypeId, DeclaringUnit.TimeDelta.TypeId, right);
    }

    protected override SpellkitObject AddOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (right is SpellkitTimeDelta td)
        {
            try
            {
                return new SpellkitDateTime(this, ((SpellkitDateTime)left).TotalTicks + td.TotalTicks);
            }
            catch (ArgumentOutOfRangeException)
            {
                return ctx.InvalidValue(right);
            }
        }

        return ctx.InvalidType(DeclaringUnit.TimeDelta.TypeId, right);
    }

    protected override SpellkitObject CastOp(ExecutionContext ctx, SpellkitObject self, SpellkitTypeInfo targetType)
    {
        if (targetType.ReflectedTypeId == DeclaringUnit.Date.ReflectedTypeId)
        {
            return ((SpellkitDateTime)self).GetDate(DeclaringUnit.Date);
        }
        else if (targetType.ReflectedTypeId == DeclaringUnit.Time.ReflectedTypeId)
        {
            return ((SpellkitDateTime)self).GetTime(DeclaringUnit.Time);
        }

        return base.CastOp(ctx, self, targetType);
    }
    #endregion

    [SpellkitMethod("Add")]
    internal static SpellkitObject AddTo(ExecutionContext ctx, SpellkitObject self, int years = 0, int months = 0, int days = 0,
         double hours = 0, double minutes = 0, double seconds = 0, double milliseconds = 0, long ticks = 0)
    {
        var s = (SpellkitDateTime)self.Clone();

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

    [SpellkitProperty]
    internal static int Year(SpellkitDateTime self) => self.Year;

    [SpellkitProperty]
    internal static int Month(SpellkitDateTime self) => self.Month;

    [SpellkitProperty]
    internal static int Day(SpellkitDateTime self) => self.Day;

    [SpellkitProperty]
    internal static string DayOfWeek(SpellkitDateTime self) => self.DayOfWeek;

    [SpellkitProperty]
    internal static int DayOfYear(SpellkitDateTime self) => self.DayOfYear;

    [SpellkitProperty]
    internal static int Hour(SpellkitDateTime self) => self.Hours;

    [SpellkitProperty]
    internal static int Minute(SpellkitDateTime self) => self.Minutes;

    [SpellkitProperty]
    internal static int Second(SpellkitDateTime self) => self.Seconds;

    [SpellkitProperty]
    internal static int Millisecond(SpellkitDateTime self) => self.Milliseconds;

    [SpellkitProperty]
    internal static int Tick(SpellkitDateTime self) => self.Ticks;

    [SpellkitProperty]
    internal static long TotalTicks(SpellkitDateTime self) => self.TotalTicks;

    [SpellkitProperty]
    internal static SpellkitObject Date(ExecutionContext ctx, SpellkitDateTime self) => new SpellkitDate(ctx.Type<SpellkitDateTypeInfo>(), new DateTime(self.TotalTicks));

    [SpellkitProperty]
    internal static SpellkitObject Time(ExecutionContext ctx, SpellkitDateTime self) => new SpellkitTime(ctx.Type<SpellkitTimeTypeInfo>(), TimeOnly.FromDateTime(new DateTime(self.TotalTicks)).Ticks);

    [SpellkitStaticMethod]
    internal static SpellkitObject Parse(ExecutionContext ctx, string input, string format)
    {
        try
        {
            return SpellkitDateTime.Parse(ctx.Type<SpellkitDateTimeTypeInfo>(), format, input);
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

    [SpellkitStaticMethod(DateTimeType)]
    internal static SpellkitObject CreateNew(ExecutionContext ctx, int year, int month, int day,
        int hour = 0, int minute = 0, int second = 0, int millisecond = 0)
    {
        var dt = new DateTime(year, month, day, hour, minute, second, millisecond, DateTimeKind.Utc);
        return new SpellkitDateTime(ctx.Type<SpellkitDateTimeTypeInfo>(), dt.Ticks);
    }

    [SpellkitStaticMethod]
    internal static SpellkitObject FromTicks(ExecutionContext ctx, long ticks) =>
        new SpellkitDateTime(ctx.Type<SpellkitDateTimeTypeInfo>(), ticks);

    [SpellkitStaticProperty]
    internal static SpellkitDateTime Default(ExecutionContext ctx) => Min(ctx);

    [SpellkitStaticProperty]
    internal static SpellkitDateTime Min(ExecutionContext ctx) => new(ctx.Type<SpellkitDateTimeTypeInfo>(), DateTime.MinValue.Ticks);

    [SpellkitStaticProperty]
    internal static SpellkitDateTime Max(ExecutionContext ctx) => new(ctx.Type<SpellkitDateTimeTypeInfo>(), DateTime.MaxValue.Ticks);

    [SpellkitStaticMethod]
    internal static SpellkitDateTime UtcNow(ExecutionContext ctx) =>
        new(ctx.Type<SpellkitDateTimeTypeInfo>(), DateTime.UtcNow.Ticks);

    [SpellkitMethod]
    internal static SpellkitObject ToLocal(
        ExecutionContext ctx,
        SpellkitDateTime self,
        SpellkitTimeDelta? offset = null) =>
        SpellkitLocalDateTimeTypeInfo.FromUtc(ctx, self, offset);
}

[SpellkitType]
public sealed partial class SpellkitLocalDateTimeTypeInfo : SpanTypeInfo<SpellkitDateTime>
{
    private const string LocalDateTime = nameof(LocalDateTime);

    public SpellkitTimeDeltaTypeInfo TypeDeltaTypeInfo => DeclaringUnit.TimeDelta;

    public SpellkitLocalDateTimeTypeInfo() : base(LocalDateTime) { }

    #region Operations
    private static long GetUtcTicks(SpellkitObject value) => ((SpellkitLocalDateTime)value).UtcTicks;

    protected override SpellkitObject EqOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right) =>
        right.TypeId == left.TypeId && GetUtcTicks(left) == GetUtcTicks(right) ? True : False;

    protected override SpellkitObject NeqOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right) =>
        right.TypeId != left.TypeId || GetUtcTicks(left) != GetUtcTicks(right) ? True : False;

    protected override SpellkitObject GtOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right) =>
        right.TypeId != left.TypeId ? ctx.InvalidType(left.TypeId, right) :
        GetUtcTicks(left) > GetUtcTicks(right) ? True : False;

    protected override SpellkitObject LtOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right) =>
        right.TypeId != left.TypeId ? ctx.InvalidType(left.TypeId, right) :
        GetUtcTicks(left) < GetUtcTicks(right) ? True : False;

    protected override SpellkitObject GteOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right) =>
        right.TypeId != left.TypeId ? ctx.InvalidType(left.TypeId, right) :
        GetUtcTicks(left) >= GetUtcTicks(right) ? True : False;

    protected override SpellkitObject LteOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right) =>
        right.TypeId != left.TypeId ? ctx.InvalidType(left.TypeId, right) :
        GetUtcTicks(left) <= GetUtcTicks(right) ? True : False;

    protected override SpellkitObject SubOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        var self = (SpellkitLocalDateTime)left;

        if (right is SpellkitLocalDateTime dt)
        {
            try
            {
                return new SpellkitTimeDelta(DeclaringUnit.TimeDelta, self.UtcTicks - dt.UtcTicks);
            }
            catch (Exception)
            {
                return ctx.InvalidValue(right);
            }
        }
        else if (right is SpellkitTimeDelta td)
        {
            try
            {
                var ticks = checked(self.TotalTicks - td.TotalTicks);
                return ticks < 0 || ticks > DateTime.MaxValue.Ticks
                    ? ctx.Overflow()
                    : new SpellkitLocalDateTime(this, ticks, self.Offset);
            }
            catch (OverflowException)
            {
                return ctx.Overflow();
            }
        }

        return ctx.InvalidType(DeclaringUnit.LocalDateTime.TypeId, DeclaringUnit.TimeDelta.TypeId, right);
    }

    protected override SpellkitObject AddOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        var self = (SpellkitLocalDateTime)left;

        if (right is SpellkitTimeDelta td)
        {
            try
            {
                var ticks = checked(self.TotalTicks + td.TotalTicks);
                return ticks < 0 || ticks > DateTime.MaxValue.Ticks
                    ? ctx.Overflow()
                    : new SpellkitLocalDateTime(this, ticks, self.Offset);
            }
            catch (OverflowException)
            {
                return ctx.Overflow();
            }
        }

        return ctx.InvalidType(DeclaringUnit.TimeDelta.TypeId, right);
    }

    protected override SpellkitObject CastOp(ExecutionContext ctx, SpellkitObject self, SpellkitTypeInfo targetType)
    {
        if (targetType.ReflectedTypeId == DeclaringUnit.Date.ReflectedTypeId)
        {
            return ((SpellkitLocalDateTime)self).GetDate(DeclaringUnit.Date);
        }
        else if (targetType.ReflectedTypeId == DeclaringUnit.Time.ReflectedTypeId)
        {
            return ((SpellkitLocalDateTime)self).GetTime(DeclaringUnit.Time);
        }

        return base.CastOp(ctx, self, targetType);
    }
    #endregion

    [SpellkitMethod("Add")]
    internal static SpellkitObject AddTo(ExecutionContext ctx, SpellkitObject self, int years = 0, int months = 0, int days = 0,
    double hours = 0, double minutes = 0, double seconds = 0, double milliseconds = 0, long ticks = 0)
    {
        var s = (SpellkitLocalDateTime)self.Clone();

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

    [SpellkitProperty]
    internal static int Year(SpellkitLocalDateTime self) => self.Year;

    [SpellkitProperty]
    internal static int Month(SpellkitLocalDateTime self) => self.Month;

    [SpellkitProperty]
    internal static int Day(SpellkitLocalDateTime self) => self.Day;

    [SpellkitProperty]
    internal static string DayOfWeek(SpellkitLocalDateTime self) => self.DayOfWeek;

    [SpellkitProperty]
    internal static int DayOfYear(SpellkitLocalDateTime self) => self.DayOfYear;

    [SpellkitProperty]
    internal static int Hour(SpellkitLocalDateTime self) => self.Hours;

    [SpellkitProperty]
    internal static int Minute(SpellkitLocalDateTime self) => self.Minutes;

    [SpellkitProperty]
    internal static int Second(SpellkitLocalDateTime self) => self.Seconds;

    [SpellkitProperty]
    internal static int Millisecond(SpellkitLocalDateTime self) => self.Milliseconds;

    [SpellkitProperty]
    internal static int Tick(SpellkitLocalDateTime self) => self.Ticks;

    [SpellkitProperty]
    internal static long TotalTicks(SpellkitLocalDateTime self) => self.TotalTicks;

    [SpellkitProperty]
    internal static long UtcTicks(SpellkitLocalDateTime self) => self.UtcTicks;

    [SpellkitProperty]
    internal static SpellkitObject Date(ExecutionContext ctx, SpellkitDateTime self) => new SpellkitDate(ctx.Type<SpellkitDateTypeInfo>(), new DateTime(self.TotalTicks));

    [SpellkitProperty]
    internal static SpellkitObject Time(ExecutionContext ctx, SpellkitDateTime self) => new SpellkitTime(ctx.Type<SpellkitTimeTypeInfo>(), TimeOnly.FromDateTime(new DateTime(self.TotalTicks)).Ticks);

    [SpellkitProperty]
    internal static SpellkitObject Offset(SpellkitLocalDateTime self) => self.Offset;

    private static SpellkitTimeDelta GetOffset(
        ExecutionContext ctx,
        SpellkitTimeDelta? offset,
        DateTime localDateTime)
    {
        if (offset is null)
        {
            return new SpellkitTimeDelta(
                ctx.Type<SpellkitTimeDeltaTypeInfo>(),
                TimeZoneInfo.Local.GetUtcOffset(localDateTime).Ticks);
        }
        else
        {
            return offset;
        }
    }

    private static bool ValidateOffset(ExecutionContext ctx, SpellkitTimeDelta offset)
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

    [SpellkitStaticMethod]
    internal static SpellkitObject Parse(ExecutionContext ctx, string input, string format)
    {
        try
        {
            var result = (SpellkitLocalDateTime)SpellkitLocalDateTime.Parse(
                ctx.Type<SpellkitLocalDateTimeTypeInfo>(), format, input);
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

    [SpellkitStaticMethod(LocalDateTime)]
    internal static SpellkitObject CreateNew(ExecutionContext ctx, int year, int month, int day,
        int hour = 0, int minute = 0, int second = 0, int millisecond = 0, SpellkitTimeDelta? offset = null)
    {
        var dt = new DateTime(year, month, day, hour, minute, second, millisecond, DateTimeKind.Unspecified);
        var delta = GetOffset(ctx, offset, dt);
        return ValidateOffset(ctx, delta)
            ? new SpellkitLocalDateTime(ctx.Type<SpellkitLocalDateTimeTypeInfo>(), dt.Ticks, delta)
            : Nil;
    }

    [SpellkitStaticMethod]
    internal static SpellkitObject FromTicks(ExecutionContext ctx, long ticks, SpellkitTimeDelta? offset = null)
    {
        try
        {
            var delta = GetOffset(ctx, offset, new DateTime(ticks, DateTimeKind.Unspecified));
            return ValidateOffset(ctx, delta)
                ? new SpellkitLocalDateTime(ctx.Type<SpellkitLocalDateTimeTypeInfo>(), ticks, delta)
                : Nil;
        }
        catch (ArgumentOutOfRangeException)
        {
            return ctx.InvalidValue(ticks);
        }
    }

    [SpellkitStaticMethod]
    internal static SpellkitObject FromUtc(ExecutionContext ctx, SpellkitDateTime dateTime, SpellkitTimeDelta? offset = null)
    {
        var utc = dateTime.ToDateTime();
        offset ??= new SpellkitTimeDelta(
            ctx.Type<SpellkitTimeDeltaTypeInfo>(),
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
            return new SpellkitLocalDateTime(
                ctx.Type<SpellkitLocalDateTimeTypeInfo>(),
                localTicks,
                offset);
        }
        catch (OverflowException)
        {
            return ctx.Overflow();
        }
    }

    [SpellkitMethod]
    internal static SpellkitObject ToUtc(ExecutionContext ctx, SpellkitLocalDateTime self)
    {
        var utcTicks = self.UtcTicks;
        return utcTicks < 0 || utcTicks > DateTime.MaxValue.Ticks
            ? ctx.Overflow()
            : new SpellkitDateTime(ctx.Type<SpellkitDateTimeTypeInfo>(), utcTicks);
    }

    [SpellkitStaticMethod]
    internal static SpellkitLocalDateTime Now(ExecutionContext ctx) =>
        CreateNow(ctx);

    [SpellkitStaticProperty]
    internal static SpellkitTimeDelta SystemOffset(ExecutionContext ctx) =>
        new(ctx.Type<SpellkitTimeDeltaTypeInfo>(), DateTimeOffset.Now.Offset);

    [SpellkitStaticProperty]
    internal static SpellkitLocalDateTime Default(ExecutionContext ctx) => Min(ctx);

    [SpellkitStaticProperty]
    internal static SpellkitLocalDateTime Min(ExecutionContext ctx) =>
        new(ctx.Type<SpellkitLocalDateTimeTypeInfo>(), DateTime.MinValue.Ticks,
            new SpellkitTimeDelta(ctx.Type<SpellkitTimeDeltaTypeInfo>(), TimeSpan.Zero));

    [SpellkitStaticProperty]
    internal static SpellkitLocalDateTime Max(ExecutionContext ctx) =>
        new(ctx.Type<SpellkitLocalDateTimeTypeInfo>(), DateTime.MaxValue.Ticks,
            new SpellkitTimeDelta(ctx.Type<SpellkitTimeDeltaTypeInfo>(), TimeSpan.Zero));

    private static SpellkitLocalDateTime CreateNow(ExecutionContext ctx)
    {
        var now = DateTimeOffset.Now;
        return new(
            ctx.Type<SpellkitLocalDateTimeTypeInfo>(),
            now.DateTime.Ticks,
            new SpellkitTimeDelta(ctx.Type<SpellkitTimeDeltaTypeInfo>(), now.Offset));
    }
}

[SpellkitType]
public sealed partial class SpellkitTimeTypeInfo : SpanTypeInfo<SpellkitTime>
{
    private const string Time = nameof(Time);

    public SpellkitTimeTypeInfo() : base(Time) { }

    [SpellkitProperty]
    internal static int Hour(SpellkitTime self) => self.Hours;

    [SpellkitProperty]
    internal static int Minute(SpellkitTime self) => self.Minutes;

    [SpellkitProperty]
    internal static int Second(SpellkitTime self) => self.Seconds;

    [SpellkitProperty]
    internal static int Millisecond(SpellkitTime self) => self.Milliseconds;

    [SpellkitProperty]
    internal static int Tick(SpellkitTime self) => self.Ticks;

    [SpellkitProperty]
    internal static long TotalTicks(SpellkitTime self) => self.TotalTicks;

    [SpellkitStaticMethod(Time)]
    internal static SpellkitObject CreateNew(ExecutionContext ctx, int hour = 0, int minute = 0, int second = 0, int millisecond = 0, int tick = 0)
    {
        var ticks = tick + DT.Sum(0, hour, minute, second, millisecond);
        return new SpellkitTime(ctx.Type<SpellkitTimeTypeInfo>(), ticks);
    }

    [SpellkitStaticMethod]
    internal static SpellkitObject FromTicks(ExecutionContext ctx, long ticks) => new SpellkitTime(ctx.Type<SpellkitTimeTypeInfo>(), ticks);

    [SpellkitStaticMethod]
    internal static SpellkitObject Parse(ExecutionContext ctx, string input, string format)
    {
        try
        {
            return SpellkitTime.Parse(ctx.Type<SpellkitTimeTypeInfo>(), format, input);
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

    [SpellkitStaticProperty]
    internal static SpellkitTime Default(ExecutionContext ctx) => Min(ctx);

    [SpellkitStaticProperty]
    internal static SpellkitTime Min(ExecutionContext ctx) => new(ctx.Type<SpellkitTimeTypeInfo>(), DateTime.MinValue.TimeOfDay.Ticks);

    [SpellkitStaticProperty]
    internal static SpellkitTime Max(ExecutionContext ctx) => new(ctx.Type<SpellkitTimeTypeInfo>(), DateTime.MaxValue.TimeOfDay.Ticks);
}

[SpellkitType]
public sealed partial class SpellkitTimeDeltaTypeInfo : SpanTypeInfo<SpellkitTimeDelta>
{
    private const string TimeDelta = nameof(TimeDelta);

    public SpellkitTimeDeltaTypeInfo() : base(TimeDelta)
    {
        SetSupportedOperations(Ops.Add | Ops.Sub | Ops.Neg);
    }

    #region Operations
    protected override SpellkitObject NegOp(ExecutionContext ctx, SpellkitObject arg) => ((SpellkitTimeDelta)arg).Negate();

    protected override SpellkitObject AddOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (right.TypeId != left.TypeId)
        {
            return ctx.InvalidType(left.TypeId, right);
        }

        return new SpellkitTimeDelta(this, ((SpellkitTimeDelta)left).TotalTicks + ((SpellkitTimeDelta)right).TotalTicks);
    }

    protected override SpellkitObject SubOp(ExecutionContext ctx, SpellkitObject left, SpellkitObject right)
    {
        if (right.TypeId != left.TypeId)
        {
            return ctx.InvalidType(left.TypeId, right);
        }

        try
        {
            return new SpellkitTimeDelta(this, ((SpellkitTimeDelta)left).TotalTicks - ((SpellkitTimeDelta)right).TotalTicks);
        }
        catch (OverflowException)
        {
            return ctx.Overflow();
        }
    }
    #endregion

    [SpellkitProperty]
    internal static int Days(SpellkitTimeDelta self) => self.Days;

    [SpellkitProperty]
    internal static int Hours(SpellkitTimeDelta self) => self.Hours;

    [SpellkitProperty]
    internal static int Minutes(SpellkitTimeDelta self) => self.Minutes;

    [SpellkitProperty]
    internal static int Seconds(SpellkitTimeDelta self) => self.Seconds;

    [SpellkitProperty]
    internal static int Milliseconds(SpellkitTimeDelta self) => self.Milliseconds;

    [SpellkitProperty]
    internal static int Ticks(SpellkitTimeDelta self) => self.Ticks;

    [SpellkitProperty]
    internal static long TotalTicks(SpellkitTimeDelta self) => self.TotalTicks;

    [SpellkitMethod]
    internal static SpellkitObject Negate(SpellkitTimeDelta self) => self.Negate();

    [SpellkitStaticMethod]
    internal static SpellkitObject FromTicks(ExecutionContext ctx, long ticks) =>
        new SpellkitTimeDelta(ctx.Type<SpellkitTimeDeltaTypeInfo>(), ticks);

    [SpellkitStaticMethod]
    internal static SpellkitObject Parse(ExecutionContext ctx, string input, string format)
    {
        try
        {
            return SpellkitTimeDelta.Parse(ctx.Type<SpellkitTimeDeltaTypeInfo>(), format, input);
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

    [SpellkitStaticMethod(TimeDelta)]
    internal static SpellkitObject New(ExecutionContext ctx, int days = 0, int hours = 0, int minutes = 0,
        int seconds = 0, int milliseconds = 0, long ticks = 0)
    {
        ticks += DT.Sum(days, hours, minutes, seconds, milliseconds);
        return new SpellkitTimeDelta(ctx.Type<SpellkitTimeDeltaTypeInfo>(), ticks);
    }

    [SpellkitStaticProperty]
    internal static SpellkitTimeDelta Default(ExecutionContext ctx) => new(ctx.Type<SpellkitTimeDeltaTypeInfo>(), TimeSpan.Zero.Ticks);

    [SpellkitStaticProperty]
    internal static SpellkitTimeDelta Min(ExecutionContext ctx) => new(ctx.Type<SpellkitTimeDeltaTypeInfo>(), TimeSpan.MinValue.Ticks);

    [SpellkitStaticProperty]
    internal static SpellkitTimeDelta Max(ExecutionContext ctx) => new(ctx.Type<SpellkitTimeDeltaTypeInfo>(), TimeSpan.MaxValue.Ticks);
}
