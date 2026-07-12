using System.Collections.Generic;
using System.Text;
using static Spellkit.Library.Time.FormatElementKind;

namespace Spellkit.Library.Time;

internal enum FormatElementKind
{
    Literal,
    Sign,
    Year,
    Month,
    MonthAbbrev,
    MonthName,
    Day,
    Hour,
    Hour24,
    Minute,
    Second,
    Decisecond,
    Centisecond,
    Millisecond,
    TenthThousandth,
    HundredthThousandth,
    Microsecond,
    Tick,
    PmAm,
    Offset
}

internal record FormatElement(FormatElementKind Kind, string Value, int Padding = 1);

internal sealed class FormatParser
{
    private readonly FormatElement[] elements;
    private static readonly FormatElement[] timeDeltaElements = new FormatElement[]
    {
        new(Sign, "+"),
        new(Day, "dd", 2),
        new(Day, "d"),
        new(Hour24, "HH", 2),
        new(Hour24, "H"),
        new(Hour, "hh", 2),
        new(Hour, "h"),
        new(Minute, "mm", 2),
        new(Minute, "m"),
        new(Second, "ss", 2),
        new(Second, "s"),
        new(Tick, "fffffff", 7),
        new(Microsecond, "ffffff", 6),
        new(HundredthThousandth, "fffff", 5),
        new(TenthThousandth, "ffff", 4),
        new(Millisecond, "fff", 3),
        new(Centisecond, "ff", 2),
        new(Decisecond, "f"),
    };
    private static readonly FormatElement[] timeElements = new FormatElement[]
    {
        new(Hour24, "HH", 2),
        new(Hour24, "H"),
        new(Hour, "hh", 2),
        new(Hour, "h"),
        new(Minute, "mm", 2),
        new(Minute, "m"),
        new(Second, "ss", 2),
        new(Second, "s"),
        new(Tick, "fffffff", 7),
        new(Microsecond, "ffffff", 6),
        new(HundredthThousandth, "fffff", 5),
        new(TenthThousandth, "ffff", 4),
        new(Millisecond, "fff", 3),
        new(Centisecond, "ff", 2),
        new(Decisecond, "f"),
        new(PmAm, "tt", 2),
        new(PmAm, "t"),
    };
    private static readonly FormatElement[] dateElements = new FormatElement[]
    {
        new(Year,"yyyy", 4),
        new(Year,"yyy", 3),
        new(Year,"yy", 2),
        new(Year,"y", 1),
        new(MonthName,"MMMM"),
        new(MonthAbbrev,"MMM"),
        new(Month,"MM", 2),
        new(Month,"M", 1),
        new(Day, "dd", 2),
        new(Day, "d"),
    };
    private static readonly FormatElement[] dateTimeElements = new FormatElement[]
    {
        new(Year,"yyyy", 4),
        new(Year,"yyy", 3),
        new(Year,"yy", 2),
        new(Year,"y", 1),
        new(MonthName,"MMMM"),
        new(MonthAbbrev,"MMM"),
        new(Month,"MM", 2),
        new(Month,"M", 1),
        new(Day, "dd", 2),
        new(Day, "d"),
        new(Hour24, "HH", 2),
        new(Hour24, "H"),
        new(Hour, "hh", 2),
        new(Hour, "h"),
        new(Minute, "mm", 2),
        new(Minute, "m"),
        new(Second, "ss", 2),
        new(Second, "s"),
        new(Tick, "fffffff", 7),
        new(Microsecond, "ffffff", 6),
        new(HundredthThousandth, "fffff", 5),
        new(TenthThousandth, "ffff", 4),
        new(Millisecond, "fff", 3),
        new(Centisecond, "ff", 2),
        new(Decisecond, "f"),
        new(PmAm, "tt", 2),
        new(PmAm, "t"),
    };
    private static readonly FormatElement[] localDateElements = new FormatElement[]
    {
        new(Year,"yyyy", 4),
        new(Year,"yyy", 3),
        new(Year,"yy", 2),
        new(Year,"y", 1),
        new(MonthName,"MMMM"),
        new(MonthAbbrev,"MMM"),
        new(Month,"MM", 2),
        new(Month,"M", 1),
        new(Day, "dd", 2),
        new(Day, "d"),
        new(Offset, "zzz", 3),
        new(Offset, "zz", 2),
        new(Offset, "z"),
    };
    private static readonly FormatElement[] localDateTimeElements = new FormatElement[]
    {
        new(Year,"yyyy", 4),
        new(Year,"yyy", 3),
        new(Year,"yy", 2),
        new(Year,"y", 1),
        new(MonthName,"MMMM"),
        new(MonthAbbrev,"MMM"),
        new(Month,"MM", 2),
        new(Month,"M", 1),
        new(Day, "dd", 2),
        new(Day, "d"),
        new(Offset, "zzz", 3),
        new(Offset, "zz", 2),
        new(Offset, "z"),
        new(Hour24, "HH", 2),
        new(Hour24, "H"),
        new(Hour, "hh", 2),
        new(Hour, "h"),
        new(Minute, "mm", 2),
        new(Minute, "m"),
        new(Second, "ss", 2),
        new(Second, "s"),
        new(Tick, "fffffff", 7),
        new(Microsecond, "ffffff", 6),
        new(HundredthThousandth, "fffff", 5),
        new(TenthThousandth, "ffff", 4),
        new(Millisecond, "fff", 3),
        new(Centisecond, "ff", 2),
        new(Decisecond, "f"),
        new(PmAm, "tt", 2),
        new(PmAm, "t"),
        new(Offset, "zzz", 3),
        new(Offset, "zz", 2),
        new(Offset, "z"),
    };
    public static FormatParser TimeDeltaParser { get; } = new FormatParser(timeDeltaElements);
    public static FormatParser TimeParser { get; } = new FormatParser(timeElements);
    public static FormatParser DateParser { get; } = new FormatParser(dateElements);
    public static FormatParser DateTimeParser { get; } = new FormatParser(dateTimeElements);
    public static FormatParser LocalDateParser { get; } = new FormatParser(localDateElements);
    public static FormatParser LocalDateTimeParser { get; } = new FormatParser(localDateTimeElements);

    public FormatParser(FormatElement[] elements) => this.elements = elements;

    public List<FormatElement> ParseSpecifiers(string input)
    {
        var ret = new List<FormatElement>();

        for (var i = 0; i < input.Length; i++)
        {
            if (input[i] == '\\')
            {
                i += 1;
            }
            else
            {
                var spc = CheckSpecifiers(input, ref i);

                if (spc is not null)
                {
                    ret.Add(spc);
                    continue;
                }
            }

            ret.Add(new FormatElement(Literal, input[i].ToString()));
        }

        return ret;
    }

    private FormatElement CheckSpecifiers(string input, ref int i)
    {
        for (var j = 0; j < elements.Length; j++)
        {
            if (CheckSpecifier(input, elements[j].Value, ref i))
            {
                return elements[j];
            }
        }

        return null!;
    }

    private bool CheckSpecifier(string input, string specifier, ref int index)
    {
        var preserved = index;

        if (specifier.Length == 1)
        {
            return input[index] == specifier[0];
        }

        for (var i = 0; i < specifier.Length; i++)
        {
            if (index >= input.Length || specifier[i] != input[index++])
            {
                index = preserved;
                return false;
            }
        }

        index--;
        return true;
    }
}

internal static class Formatter
{
    public static string Format(long val, FormatElement fe)
    {
        val = Math.Abs(val);

        if (fe.Padding == 0 && val == 0)
        {
            return string.Empty;
        }

        return fe.Padding > 1 ? val.ToString().PadLeft(fe.Padding, '0') : val.ToString();
    }

    public static bool FormatInterval(this IInterval self, StringBuilder builder, FormatElement elem)
    {
        switch (elem.Kind)
        {
            case Sign:
                if (self.TotalTicks > 0)
                {
                    builder.Append('+');
                }
                else if (self.TotalTicks < 0)
                {
                    builder.Append('-');
                }

                return true;
            case Day:
                builder.Append(Format(self.Days, elem));
                return true;
            default:
                return FormatTime(self, builder, elem);
        }
    }

    public static bool FormatLocalDateTime(this ILocalDateTime self, StringBuilder builder, FormatElement elem)
    {
        if (!FormatDate(self, builder, elem))
        {
            if (!FormatTime(self, builder, elem))
            {
                if (elem.Kind == Offset)
                {
                    if (self.Interval.TotalTicks == 0)
                    {
                        return true;
                    }
                    else if (self.Interval.TotalTicks < 0)
                    {
                        builder.Append('-');
                    }
                    else
                    {
                        builder.Append('+');
                    }

                    if (elem.Padding == 1)
                    {
                        builder.Append(Math.Abs(self.Interval.Hours));
                        return true;
                    }
                    else if (elem.Padding == 2)
                    {
                        builder.Append(Math.Abs(self.Interval.Hours).ToString().PadLeft(2, '0'));
                        return true;
                    }
                    else if (elem.Padding == 3)
                    {
                        builder.Append(Math.Abs(self.Interval.Hours).ToString().PadLeft(2, '0'));
                        builder.Append(SystemCulture.DateTimeFormat.TimeSeparator);
                        builder.Append(Math.Abs(self.Interval.Minutes).ToString().PadLeft(2, '0'));
                        return true;
                    }
                }
            }
        }

        return false;
    }

    public static bool FormatDateTime(this IDateTime self, StringBuilder builder, FormatElement elem)
    {
        if (!FormatDate(self, builder, elem))
        {
            return FormatTime(self, builder, elem);
        }

        return true;
    }

    public static bool FormatDate(this IDate self, StringBuilder builder, FormatElement elem)
    {
        switch (elem.Kind)
        {
            case Year:
                if (elem.Padding == 1)
                {
                    builder.Append(self.Year % 100);
                }
                else if (elem.Padding == 2)
                {
                    builder.Append((self.Year % 100).ToString().PadLeft(2, '0'));
                }
                else if (elem.Padding == 3)
                {
                    builder.Append(self.Year.ToString().PadLeft(3, '0'));
                }
                else if (elem.Padding == 4)
                {
                    builder.Append(self.Year.ToString().PadLeft(4, '0'));
                }

                return true;
            case MonthAbbrev:
                builder.Append(new DateTime(self.Year, self.Month, self.Day).ToString("MMM", SystemCulture));
                return true;
            case MonthName:
                builder.Append(new DateTime(self.Year, self.Month, self.Day).ToString("MMMM", SystemCulture));
                return true;
            case Month:
                builder.Append(Format(self.Month, elem));
                return true;
            case Day:
                builder.Append(Format(self.Day, elem));
                return true;
            case Literal:
                builder.Append(elem.Value);
                return true;
            default:
                return false;
        }
    }

    public static bool FormatTime(this ITime self, StringBuilder builder, FormatElement elem)
    {
        switch (elem.Kind)
        {
            case Hour24:
                builder.Append(Format(self.Hours, elem));
                return true;
            case Hour:
                {
                    var dt = new DateTime(1, 1, 1, self.Hours, self.Minutes, self.Seconds);
                    if (elem.Padding == 1)
                    {
                        builder.Append(dt.ToString("%h", SystemCulture));
                    }
                    else if (elem.Padding == 2)
                    {
                        builder.Append(dt.ToString("hh", SystemCulture));
                    }

                    return true;
                }
            case Minute:
                builder.Append(Format(self.Minutes, elem));
                return true;
            case Second:
                builder.Append(Format(self.Seconds, elem));
                return true;
            case Decisecond:
                builder.Append(Format(self.Milliseconds / 100, elem));
                return true;
            case Centisecond:
                builder.Append(Format(self.Milliseconds / 10, elem));
                return true;
            case Millisecond:
                builder.Append(Format(self.Milliseconds, elem));
                return true;
            case TenthThousandth:
                builder.Append(Format(self.Microseconds / 100, elem));
                return true;
            case HundredthThousandth:
                builder.Append(Format(self.Microseconds / 10, elem));
                return true;
            case Microsecond:
                builder.Append(Format(self.Microseconds, elem));
                return true;
            case Tick:
                builder.Append(Format(self.Ticks, elem));
                return true;
            case PmAm:
                {
                    if (self.Hours >= 12)
                    {
                        builder.Append(elem.Padding == 1 ? "P" : "PM");
                    }
                    else
                    {
                        builder.Append(elem.Padding == 1 ? "A" : "AM");
                    }

                    return true;
                }
            case Literal:
                builder.Append(elem.Value);
                return true;
            default:
                return false;
        }
    }
}
