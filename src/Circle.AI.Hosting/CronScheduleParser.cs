// CronScheduleParser.cs
//
// Minimal 5-field cron expression parser. Supports:
//   *          — every unit
//   N          — fixed value
//   N,M,...    — list of values
//   */N        — step (every N units)
//   N-M        — range
//   N-M/S      — range with step
//
// Field order: minute  hour  dom  month  dow
//              0-59    0-23  1-31  1-12  0-6 (0=Sunday)
//
// No external dependencies — pure C#.

namespace Circle.AI.Hosting;

/// <summary>
/// Computes the next occurrence of a 5-field cron expression after a given
/// <see cref="DateTimeOffset"/>. Handles wildcards, lists, steps, and ranges.
/// </summary>
public static class CronScheduleParser
{
    /// <summary>
    /// Returns the earliest UTC timestamp strictly after <paramref name="after"/>
    /// that satisfies <paramref name="cronExpression"/>.
    /// </summary>
    /// <param name="cronExpression">
    /// A 5-field cron expression. Fields in order:
    /// minute (0-59), hour (0-23), day-of-month (1-31), month (1-12), day-of-week (0-6, 0=Sunday).
    /// </param>
    /// <param name="after">The reference time. The returned value is strictly greater than this.</param>
    /// <exception cref="ArgumentException">Thrown when the expression cannot be parsed.</exception>
    public static DateTimeOffset GetNextOccurrence(string cronExpression, DateTimeOffset after)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cronExpression);

        var parts = cronExpression.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5)
            throw new ArgumentException(
                $"Cron expression must have exactly 5 fields, got {parts.Length}: '{cronExpression}'",
                nameof(cronExpression));

        var minuteSet = ParseField(parts[0], 0, 59);
        var hourSet   = ParseField(parts[1], 0, 23);
        var domSet    = ParseField(parts[2], 1, 31);
        var monthSet  = ParseField(parts[3], 1, 12);
        var dowSet    = ParseField(parts[4], 0, 6);

        // Start searching from the next whole minute after `after`.
        var candidate = new DateTimeOffset(
            after.UtcDateTime.Year,
            after.UtcDateTime.Month,
            after.UtcDateTime.Day,
            after.UtcDateTime.Hour,
            after.UtcDateTime.Minute,
            0,
            TimeSpan.Zero).AddMinutes(1);

        // Cap iteration to prevent infinite loops on impossible expressions
        // (e.g., "0 9 31 2 *" — Feb 31 never exists).
        var limit = candidate.AddYears(5);

        while (candidate <= limit)
        {
            // Month check
            if (!monthSet.Contains(candidate.Month))
            {
                // Advance to the first day of the next valid month.
                candidate = AdvanceToNextMonth(candidate, monthSet);
                continue;
            }

            // Day-of-month check
            if (!domSet.Contains(candidate.Day))
            {
                candidate = candidate.AddDays(1).Date();
                continue;
            }

            // Day-of-week check
            if (!dowSet.Contains((int)candidate.DayOfWeek))
            {
                candidate = candidate.AddDays(1).Date();
                continue;
            }

            // Hour check
            if (!hourSet.Contains(candidate.Hour))
            {
                // Advance to next valid hour, resetting minutes to 0.
                candidate = AdvanceToNextHour(candidate, hourSet);
                continue;
            }

            // Minute check
            if (!minuteSet.Contains(candidate.Minute))
            {
                candidate = candidate.AddMinutes(1);
                continue;
            }

            // All fields match.
            return candidate;
        }

        throw new InvalidOperationException(
            $"No occurrence found within 5 years for cron expression '{cronExpression}'.");
    }

    // ------------------------------------------------------------------
    // Parsing helpers
    // ------------------------------------------------------------------

    /// <summary>Parses one cron field into the set of matching integer values.</summary>
    private static HashSet<int> ParseField(string field, int min, int max)
    {
        var result = new HashSet<int>();

        foreach (var part in field.Split(','))
        {
            ParsePart(part.Trim(), min, max, result);
        }

        return result;
    }

    private static void ParsePart(string part, int min, int max, HashSet<int> result)
    {
        // */N  or  N-M/S  or  N-M
        int? step = null;
        var core = part;

        var slashIdx = part.IndexOf('/');
        if (slashIdx >= 0)
        {
            if (!int.TryParse(part[(slashIdx + 1)..], out var s) || s < 1)
                throw new ArgumentException($"Invalid step in cron field part '{part}'.");
            step = s;
            core = part[..slashIdx];
        }

        int rangeMin, rangeMax;

        if (core == "*")
        {
            rangeMin = min;
            rangeMax = max;
        }
        else
        {
            var dashIdx = core.IndexOf('-');
            if (dashIdx >= 0)
            {
                if (!int.TryParse(core[..dashIdx], out rangeMin) ||
                    !int.TryParse(core[(dashIdx + 1)..], out rangeMax))
                    throw new ArgumentException($"Invalid range in cron field part '{part}'.");
            }
            else
            {
                if (!int.TryParse(core, out rangeMin))
                    throw new ArgumentException($"Invalid value in cron field part '{part}'.");
                rangeMax = rangeMin;
            }
        }

        if (rangeMin < min || rangeMax > max || rangeMin > rangeMax)
            throw new ArgumentException(
                $"Cron field value {rangeMin}-{rangeMax} out of range [{min},{max}].");

        var effectiveStep = step ?? 1;
        for (var v = rangeMin; v <= rangeMax; v += effectiveStep)
            result.Add(v);
    }

    // ------------------------------------------------------------------
    // Advancement helpers
    // ------------------------------------------------------------------

    private static DateTimeOffset AdvanceToNextMonth(DateTimeOffset dt, HashSet<int> monthSet)
    {
        var year  = dt.Year;
        var month = dt.Month + 1;

        if (month > 12) { month = 1; year++; }

        while (year < dt.Year + 6)
        {
            if (monthSet.Contains(month))
                return new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero);

            month++;
            if (month > 12) { month = 1; year++; }
        }

        throw new InvalidOperationException("No valid month found in cron expression.");
    }

    private static DateTimeOffset AdvanceToNextHour(DateTimeOffset dt, HashSet<int> hourSet)
    {
        // Try subsequent hours today.
        for (var h = dt.Hour + 1; h <= 23; h++)
        {
            if (hourSet.Contains(h))
                return new DateTimeOffset(dt.Year, dt.Month, dt.Day, h, 0, 0, TimeSpan.Zero);
        }
        // No valid hour today — move to next day, first valid hour.
        var nextDay = dt.AddDays(1).Date();
        var minHour = hourSet.Min(x => x);
        return new DateTimeOffset(nextDay.Year, nextDay.Month, nextDay.Day, minHour, 0, 0, TimeSpan.Zero);
    }
}

/// <summary>Extension helpers used internally by <see cref="CronScheduleParser"/>.</summary>
file static class DateTimeOffsetExtensions
{
    /// <summary>Returns midnight UTC for the given offset's date.</summary>
    internal static DateTimeOffset Date(this DateTimeOffset dto) =>
        new(dto.Year, dto.Month, dto.Day, 0, 0, 0, TimeSpan.Zero);
}
