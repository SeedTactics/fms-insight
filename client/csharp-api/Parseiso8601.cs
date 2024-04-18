using System;
using System.Globalization;

namespace BlackMaple.FMSInsight.API
{
    public static class ParseIso8601Duration
    {
        public static bool TryParse(string duration, out TimeSpan t)
        {
            long ticks = 0;
            t = TimeSpan.Zero;

            if (duration[0] != 'P')
                return false;

            bool inTime = false;
            for (int i = 1; i < duration.Length; i++)
            {
                if (duration[i] == 'T')
                {
                    inTime = true;
                    continue;
                }

                if (!TryParseNumber(duration, ref i, out decimal num))
                    return false;

                var period = duration[i];

                if (period == 'Y' && !inTime)
                {
                    return false; // don't support years
                }
                else if (period == 'M' && !inTime)
                {
                    return false; // don't support months
                }
                else if (period == 'W' && !inTime)
                {
                    ticks += Convert.ToInt64(TimeSpan.TicksPerDay * 7 * num);
                }
                else if (period == 'D' && !inTime)
                {
                    ticks += Convert.ToInt64(TimeSpan.TicksPerDay * num);
                }
                else if (period == 'H' && inTime)
                {
                    ticks += Convert.ToInt64(TimeSpan.TicksPerHour * num);
                }
                else if (period == 'M' && inTime)
                {
                    ticks += Convert.ToInt64(TimeSpan.TicksPerMinute * num);
                }
                else if (period == 'S' && inTime)
                {
                    ticks += Convert.ToInt64(TimeSpan.TicksPerSecond * num);
                }
                else
                {
                    return false;
                }
            }

            t = TimeSpan.FromTicks(ticks);
            return true;
        }

        private static bool TryParseNumber(string s, ref int idx, out decimal n)
        {
            bool hasDecimal = false;
            var startIdx = idx;
            for (; idx < s.Length; idx++)
            {
                var c = s[idx];
                if (c == '.' || c == ',')
                {
                    hasDecimal = true;
                }
                else if (!char.IsDigit(c))
                {
                    break;
                }
            }

            if (idx == startIdx)
            {
                n = 0;
                return false;
            }

            if (hasDecimal)
            {
                var txt = s.Substring(startIdx, idx - startIdx)
                    .Replace(",", CultureInfo.InvariantCulture.NumberFormat.NumberDecimalSeparator);
                if (
                    decimal.TryParse(
                        txt,
                        NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture,
                        out n
                    )
                )
                {
                    return true;
                }
            }
            else
            {
                if (
                    decimal.TryParse(
                        s.Substring(startIdx, idx - startIdx),
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out n
                    )
                )
                {
                    return true;
                }
            }

            n = 0;
            return false;
        }
    }
}
