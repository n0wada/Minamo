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
    SpkObject GetDate(SpkDateTypeInfo typeInfo);

    SpkObject GetTime(SpkTimeTypeInfo typeInfo);

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

public abstract class SpanTypeInfo<T> : SpkForeignTypeInfo<TimeModule>
    where T : SpkObject, ISpan, IFormattable
{
    public override string ReflectedTypeName { get; }

    protected SpanTypeInfo(string typeName)
    {
        ReflectedTypeName = typeName;
        AddMixins(Spk.Order, Spk.Equatable);
    }

    #region Operations
    protected override SpkObject ToStringOp(ExecutionContext ctx, SpkObject arg, SpkObject format)
    {
        if (format.Is(Spk.Nil))
        {
            return new SpkString(arg.ToString());
        }

        if (format.TypeId is not Spk.String and not Spk.Char)
        {
            return Nil;
        }

        try
        {
            return new SpkString(((T)arg).ToString(format.ToString(), null));
        }
        catch (FormatException)
        {
            return ctx.ParsingFailed();
        }
    }

    protected override SpkObject EqOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (right.TypeId != left.TypeId)
        {
            return SpkBool.False;
        }

        return ((T)left).TotalTicks == ((T)right).TotalTicks ? True : False;
    }

    protected override SpkObject NeqOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (right.TypeId != left.TypeId)
        {
            return SpkBool.True;
        }

        return ((T)left).TotalTicks != ((T)right).TotalTicks ? True : False;
    }

    protected override SpkObject GtOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (right.TypeId != left.TypeId)
        {
            return ctx.InvalidType(left.TypeId, right);
        }

        return ((T)left).TotalTicks > ((T)right).TotalTicks ? True : False;
    }

    protected override SpkObject LtOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (right.TypeId != left.TypeId)
        {
            return ctx.InvalidType(left.TypeId, right);
        }

        return ((T)left).TotalTicks < ((T)right).TotalTicks ? True : False;
    }

    protected override SpkObject GteOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (right.TypeId != left.TypeId)
        {
            return ctx.InvalidType(left.TypeId, right);
        }

        return ((T)left).TotalTicks >= ((T)right).TotalTicks ? True : False;
    }

    protected override SpkObject LteOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (right.TypeId != left.TypeId)
        {
            return ctx.InvalidType(left.TypeId, right);
        }

        return ((T)left).TotalTicks <= ((T)right).TotalTicks ? True : False;
    }

    protected override SpkObject CastOp(ExecutionContext ctx, SpkObject self, SpkTypeInfo targetType) =>
       targetType.ReflectedTypeId switch
       {
           Spk.Integer => SpkInteger.Get(((T)self).ToInteger()),
           _ => base.CastOp(ctx, self, targetType)
       };
    #endregion
}

public sealed class SpkDate : SpkForeignObject, IDate, IFormattable
{
    private const string DEFAULT_FORMAT = "yyyy-MM-dd";

    private int days;

    public SpkDate(SpkDateTypeInfo typeInfo, int days) : base(typeInfo) => this.days = days;

    public SpkDate(SpkDateTypeInfo typeInfo, DateTime dateTime) : this(typeInfo, DateOnly.FromDateTime(dateTime).DayNumber) { }

    public long TotalTicks => days * DT.TicksPerDay;

    public int Year => new DateTime(TotalTicks).Year;

    public int Month => new DateTime(TotalTicks).Month;

    public int Day => new DateTime(TotalTicks).Day;

    public string DayOfWeek => new DateTime(TotalTicks).DayOfWeek.ToString();

    public int DayOfYear => new DateTime(TotalTicks).DayOfYear;

    public override object ToObject() => new DateOnly(Year, Month, Day);

    public long ToInteger() => days;

    public override SpkObject Clone() => new SpkDate((SpkDateTypeInfo)TypeInfo, days);

    public override int GetHashCode() => days.GetHashCode();

    public override bool Equals(SpkObject? other) => other is SpkDate dt && dt.days == days;

    public void AddDays(int days) => SetDays(new DateTime(TotalTicks).AddDays(days).Date);

    public void AddMonths(int months) => SetDays(new DateTime(TotalTicks).AddMonths(months).Date);

    public void AddYears(int years) => SetDays(new DateTime(TotalTicks).AddYears(years).Date);

    public static SpkDate Parse(SpkDateTypeInfo typeInfo, string format, string value)
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

public class SpkDateTime : SpkForeignObject, IDateTime, IFormattable
{
    private const string FORMAT = "yyyy-MM-dd HH:mm:ss.fffffff";

    protected long ticks;

    internal SpkDateTime(SpanTypeInfo<SpkDateTime> typeInfo, long ticks) : base(typeInfo) =>
        this.ticks = ticks;

    public DateTime ToDateTime() => new(ticks);

    public override object ToObject() => ToDateTime();

    public override SpkObject Clone() => new SpkDateTime((SpanTypeInfo<SpkDateTime>)TypeInfo, ticks);

    public override bool Equals(SpkObject? other) => other is SpkDateTime dt && dt.ticks == ticks;

    public override int GetHashCode() => ticks.GetHashCode();

    public override string ToString() => ToString(FORMAT);

    public long ToInteger() => ticks;

    public static SpkDateTime Parse(SpkForeignTypeInfo typeInfo, string format, string value)
    {
        var (ticks, _, _) = InputParser.Parse(FormatParser.DateTimeParser, format, value);
        return new((SpanTypeInfo<SpkDateTime>)typeInfo, ticks);
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

    public virtual SpkDateTime FirstDayOfMonth()
    {
        var dt = new DateTime(ticks, DateTimeKind.Unspecified);
        return new SpkDateTime((SpanTypeInfo<SpkDateTime>)TypeInfo, dt.AddDays(-dt.Day + 1).Ticks);
    }

    public virtual SpkDateTime LastDayOfMonth()
    {
        var dt = new DateTime(ticks, DateTimeKind.Unspecified);
        return new SpkDateTime((SpanTypeInfo<SpkDateTime>)TypeInfo, dt.AddDays(DateTime.DaysInMonth(dt.Year, dt.Month) - dt.Day).Ticks);
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

    public SpkObject GetDate(SpkDateTypeInfo typeInfo) =>
        new SpkDate(typeInfo, DateOnly.FromDateTime(new DateTime(ticks)).DayNumber);

    public SpkObject GetTime(SpkTimeTypeInfo typeInfo) =>
        new SpkTime(typeInfo, TimeOnly.FromDateTime(new DateTime(ticks)).Ticks);

    private void SetTicks(DateTime dt) => ticks = dt.Ticks;
    #endregion
}

public sealed class SpkLocalDateTime : SpkDateTime, ILocalDateTime
{
    private const string FORMAT = "yyyy-MM-dd HH:mm:ss.fffffffzzz";
    
    public SpkTimeDelta Offset { get; }

    IInterval ILocalDateTime.Interval => Offset;

    internal SpkLocalDateTime(SpkLocalDateTimeTypeInfo typeInfo, long ticks, SpkTimeDelta offset)
        : base(typeInfo, ticks) => this.Offset = offset;

    public override bool Equals(SpkObject? other) => other is SpkLocalDateTime dt
        && dt.ticks == ticks && dt.Offset.Equals(Offset);

    public static new SpkDateTime Parse(SpkForeignTypeInfo typeInfo, string format, string value)
    {
        var ti = (SpkLocalDateTimeTypeInfo)typeInfo;
        var (ticks, offset, hasOffset) =
            InputParser.Parse(FormatParser.LocalDateTimeParser, format, value);
        var resolvedOffset = hasOffset
            ? TimeSpan.FromTicks(offset)
            : TimeZoneInfo.Local.GetUtcOffset(new DateTime(ticks, DateTimeKind.Unspecified));
        return new SpkLocalDateTime(ti, ticks,
            new SpkTimeDelta(ti.TypeDeltaTypeInfo, resolvedOffset));
    }

    public override SpkObject Clone() => 
        new SpkLocalDateTime((SpkLocalDateTimeTypeInfo)TypeInfo, ticks, Offset);

    public override int GetHashCode() => ticks.GetHashCode();

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

    public override SpkDateTime FirstDayOfMonth()
    {
        var dt = new DateTime(ticks, DateTimeKind.Unspecified);
        return new SpkLocalDateTime((SpkLocalDateTimeTypeInfo)TypeInfo, dt.AddDays(-dt.Day + 1).Ticks, Offset);
    }

    public override SpkDateTime LastDayOfMonth()
    {
        var dt = new DateTime(ticks, DateTimeKind.Unspecified);
        return new SpkLocalDateTime((SpkLocalDateTimeTypeInfo)TypeInfo, dt.AddDays(DateTime.DaysInMonth(dt.Year, dt.Month) - dt.Day).Ticks, Offset);
    }
}

public sealed class SpkTime : SpkForeignObject, ITime, IFormattable
{
    private const string DEFAULT_FORMAT = "hh:mm:ss.fffffff";

    private readonly long ticks;

    public SpkTime(SpkTimeTypeInfo typeInfo, long ticks) : base(typeInfo) => this.ticks = ticks;

    public long TotalTicks => ticks;

    public int Ticks => (int)(ticks % 10_000_000);

    public int Microseconds => (int)(ticks / DT.TicksPerMicrosecond % 1_000_000);

    public int Milliseconds => (int)(ticks / DT.TicksPerMillisecond % 1000);

    public int Seconds => (int)(ticks / DT.TicksPerSecond % 60);

    public int Minutes => (int)(ticks / DT.TicksPerMinute % 60);

    public int Hours => (int)(ticks / DT.TicksPerHour);

    public override object ToObject() => new TimeOnly(ticks);

    public long ToInteger() => ticks;

    public override SpkObject Clone() => this;

    public override int GetHashCode() => ticks.GetHashCode();

    public override bool Equals(SpkObject? other) => other is SpkTime dt && dt.ticks == ticks;
    
    public static SpkTime Parse(SpkTimeTypeInfo typeInfo, string format, string value)
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

public sealed class SpkTimeDelta : SpkForeignObject, IInterval, IFormattable
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

    public SpkTimeDelta(SpkTimeDeltaTypeInfo typeInfo, long ticks) : base(typeInfo) => this.ticks = ticks;

    public SpkTimeDelta(SpkTimeDeltaTypeInfo typeInfo, TimeSpan timeSpan) : this(typeInfo, timeSpan.Ticks) { }
    
    public override object ToObject() => ToTimeSpan();

    public long ToInteger() => ticks;

    public TimeSpan ToTimeSpan() => TimeSpan.FromTicks(ticks);

    public SpkTimeDelta Negate() => new((SpkTimeDeltaTypeInfo)TypeInfo, -ticks);

    public static SpkTimeDelta Parse(SpkTimeDeltaTypeInfo typeInfo, string format, string value)
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

    public override bool Equals(SpkObject? other) => other is SpkTimeDelta d && d.ticks == ticks;

    public override SpkObject Clone() => this;
}

[SpkType]
public sealed partial class SpkCalendarTypeInfo : SpkForeignTypeInfo<TimeModule>
{
    public override string ReflectedTypeName => "Calendar";

    [SpkStaticMethod]
    internal static int DaysInMonth(int year, int month) => DateTime.DaysInMonth(year, month);

    [SpkStaticMethod]
    internal static SpkObject FirstDayOfMonth(SpkDateTime value) => value.FirstDayOfMonth();

    [SpkStaticMethod]
    internal static SpkObject LastDayOfMonth(SpkDateTime value) => value.LastDayOfMonth();

    [SpkStaticMethod]
    internal static int DaysInYear(int year) => DateTime.IsLeapYear(year) ? 366 : 365;

    [SpkStaticMethod]
    internal static bool IsLeapYear(int year) => DateTime.IsLeapYear(year);

    [SpkStaticMethod]
    internal static SpkObject ParseDateTime(ExecutionContext ctx, string input, string format)
    {
        var (ticks, offset, hasOffset) =
            InputParser.Parse(FormatParser.LocalDateTimeParser, format, input);
        
        if (!hasOffset)
        {
            return new SpkDateTime(ctx.Type<SpkDateTimeTypeInfo>(), ticks);
        }
        else
        {
            return new SpkLocalDateTime(ctx.Type<SpkLocalDateTimeTypeInfo>(), ticks,
                new SpkTimeDelta(ctx.Type<SpkTimeDeltaTypeInfo>(), offset));
        }
    }
}

[SpkType]
public sealed partial class SpkDateTypeInfo : SpanTypeInfo<SpkDate>
{
    private const string Date = nameof(Date);

    public SpkDateTypeInfo() : base(Date) { }

    [SpkMethod("Add")]
    internal static SpkObject AddTo(ExecutionContext ctx, SpkObject self, int years = 0, int months = 0, int days = 0)
    {
        var s = (SpkDate)self.Clone();

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

    [SpkProperty]
    internal static int Year(SpkDate self) => self.Year;

    [SpkProperty]
    internal static int Month(SpkDate self) => self.Month;

    [SpkProperty]
    internal static int Day(SpkDate self) => self.Day;

    [SpkProperty]
    internal static string DayOfWeek(SpkDate self) => self.DayOfWeek;

    [SpkProperty]
    internal static int DayOfYear(SpkDate self) => self.DayOfYear;

    [SpkStaticMethod]
    internal static SpkObject Parse(ExecutionContext ctx, string input, string format)
    {
        try
        {
            return SpkDate.Parse(ctx.Type<SpkDateTypeInfo>(), format, input);
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

    [SpkStaticMethod(Date)]
    internal static SpkObject CreateNew(ExecutionContext ctx, int year, int month, int day)
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

        return new SpkDate(ctx.Type<SpkDateTypeInfo>(), (int)(dt.Ticks / DT.TicksPerDay));
    }

    [SpkStaticProperty]
    internal static SpkDate Default(ExecutionContext ctx) => Min(ctx);
    
    [SpkStaticProperty]
    internal static SpkDate Min(ExecutionContext ctx) => new(ctx.Type<SpkDateTypeInfo>(), (int)(DateTime.MinValue.Date.Ticks / DT.TicksPerDay));

    [SpkStaticProperty]
    internal static SpkDate Max(ExecutionContext ctx) => new(ctx.Type<SpkDateTypeInfo>(), (int)(DateTime.MaxValue.Date.Ticks / DT.TicksPerDay));
}

[SpkType]
public sealed partial class SpkDateTimeTypeInfo : SpanTypeInfo<SpkDateTime>
{
    private const string DateTimeType = "DateTime";

    public SpkDateTimeTypeInfo() : base(DateTimeType)
    {
        SetSupportedOperations(Ops.Sub | Ops.Add);
    }

    #region Operations
    protected override SpkObject SubOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (right is SpkDateTime dt)
        {
            try
            {
                return new SpkTimeDelta(DeclaringUnit.TimeDelta, ((SpkDateTime)left).TotalTicks - dt.TotalTicks);
            }
            catch (Exception)
            {
                return ctx.InvalidValue(right);
            }
        }
        else if (right is SpkTimeDelta td)
        {
            try
            {
                return new SpkDateTime(this, ((SpkDateTime)left).TotalTicks - td.TotalTicks);
            }
            catch (Exception)
            {
                return ctx.InvalidValue(right);
            }
        }

        return ctx.InvalidType(DeclaringUnit.DateTime.TypeId, DeclaringUnit.TimeDelta.TypeId, right);
    }

    protected override SpkObject AddOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (right is SpkTimeDelta td)
        {
            try
            {
                return new SpkDateTime(this, ((SpkDateTime)left).TotalTicks + td.TotalTicks);
            }
            catch (ArgumentOutOfRangeException)
            {
                return ctx.InvalidValue(right);
            }
        }

        return ctx.InvalidType(DeclaringUnit.TimeDelta.TypeId, right);
    }

    protected override SpkObject CastOp(ExecutionContext ctx, SpkObject self, SpkTypeInfo targetType)
    {
        if (targetType.ReflectedTypeId == DeclaringUnit.Date.ReflectedTypeId)
        {
            return ((SpkDateTime)self).GetDate(DeclaringUnit.Date);
        }
        else if (targetType.ReflectedTypeId == DeclaringUnit.Time.ReflectedTypeId)
        {
            return ((SpkDateTime)self).GetTime(DeclaringUnit.Time);
        }

        return base.CastOp(ctx, self, targetType);
    }
    #endregion

    [SpkMethod("Add")]
    internal static SpkObject AddTo(ExecutionContext ctx, SpkObject self, int years = 0, int months = 0, int days = 0,
         double hours = 0, double minutes = 0, double seconds = 0, double milliseconds = 0, long ticks = 0)
    {
        var s = (SpkDateTime)self.Clone();

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

    [SpkProperty]
    internal static int Year(SpkDateTime self) => self.Year;

    [SpkProperty]
    internal static int Month(SpkDateTime self) => self.Month;

    [SpkProperty]
    internal static int Day(SpkDateTime self) => self.Day;

    [SpkProperty]
    internal static string DayOfWeek(SpkDateTime self) => self.DayOfWeek;

    [SpkProperty]
    internal static int DayOfYear(SpkDateTime self) => self.DayOfYear;

    [SpkProperty]
    internal static int Hour(SpkDateTime self) => self.Hours;

    [SpkProperty]
    internal static int Minute(SpkDateTime self) => self.Minutes;

    [SpkProperty]
    internal static int Second(SpkDateTime self) => self.Seconds;

    [SpkProperty]
    internal static int Millisecond(SpkDateTime self) => self.Milliseconds;

    [SpkProperty]
    internal static int Tick(SpkDateTime self) => self.Ticks;

    [SpkProperty]
    internal static long TotalTicks(SpkDateTime self) => self.TotalTicks;

    [SpkProperty]
    internal static SpkObject Date(ExecutionContext ctx, SpkDateTime self) => new SpkDate(ctx.Type<SpkDateTypeInfo>(), new DateTime(self.TotalTicks));

    [SpkProperty]
    internal static SpkObject Time(ExecutionContext ctx, SpkDateTime self) => new SpkTime(ctx.Type<SpkTimeTypeInfo>(), TimeOnly.FromDateTime(new DateTime(self.TotalTicks)).Ticks);

    [SpkStaticMethod]
    internal static SpkObject Parse(ExecutionContext ctx, string input, string format)
    {
        try
        {
            return SpkDateTime.Parse(ctx.Type<SpkDateTimeTypeInfo>(), format, input);
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

    [SpkStaticMethod(DateTimeType)]
    internal static SpkObject CreateNew(ExecutionContext ctx, int year, int month, int day,
        int hour = 0, int minute = 0, int second = 0, int millisecond = 0)
    {
        var dt = new DateTime(year, month, day, hour, minute, second, millisecond, DateTimeKind.Utc);
        return new SpkDateTime(ctx.Type<SpkDateTimeTypeInfo>(), dt.Ticks);
    }

    [SpkStaticMethod]
    internal static SpkObject FromTicks(ExecutionContext ctx, long ticks) =>
        new SpkDateTime(ctx.Type<SpkDateTimeTypeInfo>(), ticks);

    [SpkStaticProperty]
    internal static SpkDateTime Default(ExecutionContext ctx) => Min(ctx);

    [SpkStaticProperty]
    internal static SpkDateTime Min(ExecutionContext ctx) => new(ctx.Type<SpkDateTimeTypeInfo>(), DateTime.MinValue.Ticks);

    [SpkStaticProperty]
    internal static SpkDateTime Max(ExecutionContext ctx) => new(ctx.Type<SpkDateTimeTypeInfo>(), DateTime.MaxValue.Ticks);

    [SpkStaticMethod]
    internal static SpkDateTime Now(ExecutionContext ctx) => new(ctx.Type<SpkDateTimeTypeInfo>(), DateTime.UtcNow.Ticks);
}

[SpkType]
public sealed partial class SpkLocalDateTimeTypeInfo : SpanTypeInfo<SpkDateTime>
{
    private const string LocalDateTime = nameof(LocalDateTime);

    public SpkTimeDeltaTypeInfo TypeDeltaTypeInfo => DeclaringUnit.TimeDelta;

    public SpkLocalDateTimeTypeInfo() : base(LocalDateTime) { }

    #region Operations
    protected override SpkObject SubOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        var self = (SpkLocalDateTime)left;

        if (right is SpkLocalDateTime dt)
        {
            try
            {
                if (!self.Offset.Equals(dt.Offset))
                {
                    return ctx.InvalidValue(right);
                }

                return new SpkTimeDelta(DeclaringUnit.TimeDelta, self.Ticks - dt.Ticks);
            }
            catch (Exception)
            {
                return ctx.InvalidValue(right);
            }
        }
        else if (right is SpkTimeDelta td)
        {
            try
            {
                return new SpkLocalDateTime(this, self.Ticks - td.TotalTicks, self.Offset);
            }
            catch (Exception)
            {
                return ctx.InvalidValue(right);
            }
        }

        return ctx.InvalidType(DeclaringUnit.LocalDateTime.TypeId, DeclaringUnit.TimeDelta.TypeId, right);
    }

    protected override SpkObject AddOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        var self = (SpkLocalDateTime)left;
        
        if (right is SpkTimeDelta td)
        {
            try
            {
                if (self.Offset.Ticks != td.TotalTicks)
                {
                    return ctx.InvalidValue(right);
                }

                return new SpkLocalDateTime(this, self.Ticks + td.TotalTicks, self.Offset);
            }
            catch (ArgumentOutOfRangeException)
            {
                return ctx.InvalidValue(right);
            }
        }

        return ctx.InvalidType(DeclaringUnit.TimeDelta.TypeId, right);
    }

    protected override SpkObject CastOp(ExecutionContext ctx, SpkObject self, SpkTypeInfo targetType)
    {
        if (targetType.ReflectedTypeId == DeclaringUnit.Date.ReflectedTypeId)
        {
            return ((SpkLocalDateTime)self).GetDate(DeclaringUnit.Date);
        }
        else if (targetType.ReflectedTypeId == DeclaringUnit.Time.ReflectedTypeId)
        {
            return ((SpkLocalDateTime)self).GetTime(DeclaringUnit.Time);
        }

        return base.CastOp(ctx, self, targetType);
    }
    #endregion

    [SpkMethod("Add")]
    internal static SpkObject AddTo(ExecutionContext ctx, SpkObject self, int years = 0, int months = 0, int days = 0,
    double hours = 0, double minutes = 0, double seconds = 0, double milliseconds = 0, long ticks = 0)
    {
        var s = (SpkLocalDateTime)self.Clone();

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

    [SpkProperty]
    internal static int Year(SpkLocalDateTime self) => self.Year;

    [SpkProperty]
    internal static int Month(SpkLocalDateTime self) => self.Month;

    [SpkProperty]
    internal static int Day(SpkLocalDateTime self) => self.Day;

    [SpkProperty]
    internal static string DayOfWeek(SpkLocalDateTime self) => self.DayOfWeek;

    [SpkProperty]
    internal static int DayOfYear(SpkLocalDateTime self) => self.DayOfYear;

    [SpkProperty]
    internal static int Hour(SpkLocalDateTime self) => self.Hours;

    [SpkProperty]
    internal static int Minute(SpkLocalDateTime self) => self.Minutes;

    [SpkProperty]
    internal static int Second(SpkLocalDateTime self) => self.Seconds;

    [SpkProperty]
    internal static int Millisecond(SpkLocalDateTime self) => self.Milliseconds;

    [SpkProperty]
    internal static int Tick(SpkLocalDateTime self) => self.Ticks;

    [SpkProperty]
    internal static long TotalTicks(SpkLocalDateTime self) => self.TotalTicks;

    [SpkProperty]
    internal static SpkObject Date(ExecutionContext ctx, SpkDateTime self) => new SpkDate(ctx.Type<SpkDateTypeInfo>(), new DateTime(self.TotalTicks));

    [SpkProperty]
    internal static SpkObject Time(ExecutionContext ctx, SpkDateTime self) => new SpkTime(ctx.Type<SpkTimeTypeInfo>(), TimeOnly.FromDateTime(new DateTime(self.TotalTicks)).Ticks);

    [SpkProperty]
    internal static SpkObject Offset(SpkLocalDateTime self) => self.Offset;

    private static SpkTimeDelta GetOffset(
        ExecutionContext ctx,
        SpkTimeDelta? offset,
        DateTime localDateTime)
    {
        if (offset is null)
        {
            return new SpkTimeDelta(
                ctx.Type<SpkTimeDeltaTypeInfo>(),
                TimeZoneInfo.Local.GetUtcOffset(localDateTime).Ticks);
        }
        else
        {
            return offset;
        }
    }

    [SpkStaticMethod]
    internal static SpkObject Parse(ExecutionContext ctx, string input, string format)
    {
        try
        {
            return SpkLocalDateTime.Parse(ctx.Type<SpkLocalDateTimeTypeInfo>(), format, input);
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

    [SpkStaticMethod(LocalDateTime)]
    internal static SpkObject CreateNew(ExecutionContext ctx, int year, int month, int day,
        int hour = 0, int minute = 0, int second = 0, int millisecond = 0, SpkTimeDelta? offset = null)
    {
        var dt = new DateTime(year, month, day, hour, minute, second, millisecond, DateTimeKind.Unspecified);
        var delta = GetOffset(ctx, offset, dt);
        return new SpkLocalDateTime(ctx.Type<SpkLocalDateTimeTypeInfo>(), dt.Ticks, delta);
    }

    [SpkStaticMethod]
    internal static SpkObject FromTicks(ExecutionContext ctx, long ticks, SpkTimeDelta? offset = null)
    {
        var delta = GetOffset(ctx, offset, new DateTime(ticks, DateTimeKind.Unspecified));
        return new SpkLocalDateTime(ctx.Type<SpkLocalDateTimeTypeInfo>(), ticks, delta);
    }

    [SpkStaticMethod]
    internal static SpkObject FromDateTime(ExecutionContext ctx, SpkDateTime dateTime, SpkTimeDelta? offset = null)
    {
        var dt = dateTime.ToDateTime();
        offset = GetOffset(ctx, offset, dt);
        var td = offset.ToTimeSpan();
        var targetZone = TimeZoneInfo.CreateCustomTimeZone(Guid.NewGuid().ToString(), td, null, null);
        var dat = TimeZoneInfo.ConvertTimeFromUtc(dt, targetZone);
        return new SpkLocalDateTime(ctx.Type<SpkLocalDateTimeTypeInfo>(), dat.Ticks,
            new SpkTimeDelta(ctx.Type<SpkTimeDeltaTypeInfo>(), targetZone.BaseUtcOffset));
    }

    [SpkStaticMethod]
    internal static SpkLocalDateTime Now(ExecutionContext ctx) =>
        CreateNow(ctx);

    [SpkStaticProperty]
    internal static SpkTimeDelta LocalOffset(ExecutionContext ctx) =>
        new(ctx.Type<SpkTimeDeltaTypeInfo>(), TimeZoneInfo.Local.GetUtcOffset(DateTime.Now));

    [SpkStaticProperty]
    internal static SpkLocalDateTime Default(ExecutionContext ctx) => Min(ctx);

    [SpkStaticProperty]
    internal static SpkLocalDateTime Min(ExecutionContext ctx) =>
        new(ctx.Type<SpkLocalDateTimeTypeInfo>(), DateTime.MinValue.Ticks,
            GetOffset(ctx, null, DateTime.MinValue));

    [SpkStaticProperty]
    internal static SpkLocalDateTime Max(ExecutionContext ctx) =>
        new(ctx.Type<SpkLocalDateTimeTypeInfo>(), DateTime.MaxValue.Ticks,
            GetOffset(ctx, null, DateTime.MaxValue));

    private static SpkLocalDateTime CreateNow(ExecutionContext ctx)
    {
        var now = DateTime.Now;
        return new(ctx.Type<SpkLocalDateTimeTypeInfo>(), now.Ticks, GetOffset(ctx, null, now));
    }
}

[SpkType]
public sealed partial class SpkTimeTypeInfo : SpanTypeInfo<SpkTime>
{
    private const string Time = nameof(Time);

    public SpkTimeTypeInfo() : base(Time) { }

    [SpkProperty]
    internal static int Hour(SpkTime self) => self.Hours;

    [SpkProperty]
    internal static int Minute(SpkTime self) => self.Minutes;

    [SpkProperty]
    internal static int Second(SpkTime self) => self.Seconds;

    [SpkProperty]
    internal static int Millisecond(SpkTime self) => self.Milliseconds;

    [SpkProperty]
    internal static int Tick(SpkTime self) => self.Ticks;

    [SpkProperty]
    internal static long TotalTicks(SpkTime self) => self.TotalTicks;

    [SpkStaticMethod(Time)]
    internal static SpkObject CreateNew(ExecutionContext ctx, int hour = 0, int minute = 0, int second = 0, int millisecond = 0, int tick = 0)
    {
        var ticks = tick + DT.Sum(0, hour, minute, second, millisecond);
        return new SpkTime(ctx.Type<SpkTimeTypeInfo>(), ticks);
    }

    [SpkStaticMethod]
    internal static SpkObject FromTicks(ExecutionContext ctx, long ticks) => new SpkTime(ctx.Type<SpkTimeTypeInfo>(), ticks);

    [SpkStaticMethod]
    internal static SpkObject Parse(ExecutionContext ctx, string input, string format)
    {
        try
        {
            return SpkTime.Parse(ctx.Type<SpkTimeTypeInfo>(), format, input);
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

    [SpkStaticProperty]
    internal static SpkTime Default(ExecutionContext ctx) => Min(ctx);

    [SpkStaticProperty]
    internal static SpkTime Min(ExecutionContext ctx) => new(ctx.Type<SpkTimeTypeInfo>(), DateTime.MinValue.TimeOfDay.Ticks);

    [SpkStaticProperty]
    internal static SpkTime Max(ExecutionContext ctx) => new(ctx.Type<SpkTimeTypeInfo>(), DateTime.MaxValue.TimeOfDay.Ticks);
}

[SpkType]
public sealed partial class SpkTimeDeltaTypeInfo : SpanTypeInfo<SpkTimeDelta>
{
    private const string TimeDelta = nameof(TimeDelta);

    public SpkTimeDeltaTypeInfo() : base(TimeDelta)
    {
        SetSupportedOperations(Ops.Add | Ops.Sub | Ops.Neg);
    }

    #region Operations
    protected override SpkObject NegOp(ExecutionContext ctx, SpkObject arg) => ((SpkTimeDelta)arg).Negate();

    protected override SpkObject AddOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (right.TypeId != left.TypeId)
        {
            return ctx.InvalidType(left.TypeId, right);
        }

        return new SpkTimeDelta(this, ((SpkTimeDelta)left).TotalTicks + ((SpkTimeDelta)right).TotalTicks);
    }

    protected override SpkObject SubOp(ExecutionContext ctx, SpkObject left, SpkObject right)
    {
        if (right.TypeId != left.TypeId)
        {
            return ctx.InvalidType(left.TypeId, right);
        }

        try
        {
            return new SpkTimeDelta(this, ((SpkTimeDelta)left).TotalTicks - ((SpkTimeDelta)right).TotalTicks);
        }
        catch (OverflowException)
        {
            return ctx.Overflow();
        }
    }
    #endregion

    [SpkProperty]
    internal static int Days(SpkTimeDelta self) => self.Days;

    [SpkProperty]
    internal static int Hours(SpkTimeDelta self) => self.Hours;

    [SpkProperty]
    internal static int Minutes(SpkTimeDelta self) => self.Minutes;

    [SpkProperty]
    internal static int Seconds(SpkTimeDelta self) => self.Seconds;

    [SpkProperty]
    internal static int Milliseconds(SpkTimeDelta self) => self.Milliseconds;

    [SpkProperty]
    internal static int Ticks(SpkTimeDelta self) => self.Ticks;

    [SpkProperty]
    internal static long TotalTicks(SpkTimeDelta self) => self.TotalTicks;

    [SpkMethod]
    internal static SpkObject Negate(SpkTimeDelta self) => self.Negate();

    [SpkStaticMethod]
    internal static SpkObject FromTicks(ExecutionContext ctx, long ticks) =>
        new SpkTimeDelta(ctx.Type<SpkTimeDeltaTypeInfo>(), ticks);

    [SpkStaticMethod]
    internal static SpkObject Parse(ExecutionContext ctx, string input, string format)
    {
        try
        {
            return SpkTimeDelta.Parse(ctx.Type<SpkTimeDeltaTypeInfo>(), format, input);
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

    [SpkStaticMethod(TimeDelta)]
    internal static SpkObject New(ExecutionContext ctx, int days = 0, int hours = 0, int minutes = 0,
        int seconds = 0, int milliseconds = 0, long ticks = 0)
    {
        ticks += DT.Sum(days, hours, minutes, seconds, milliseconds);
        return new SpkTimeDelta(ctx.Type<SpkTimeDeltaTypeInfo>(), ticks);
    }

    [SpkStaticProperty]
    internal static SpkTimeDelta Default(ExecutionContext ctx) => new(ctx.Type<SpkTimeDeltaTypeInfo>(), TimeSpan.Zero.Ticks);

    [SpkStaticProperty]
    internal static SpkTimeDelta Min(ExecutionContext ctx) => new(ctx.Type<SpkTimeDeltaTypeInfo>(), TimeSpan.MinValue.Ticks);

    [SpkStaticProperty]
    internal static SpkTimeDelta Max(ExecutionContext ctx) => new(ctx.Type<SpkTimeDeltaTypeInfo>(), TimeSpan.MaxValue.Ticks);
}
