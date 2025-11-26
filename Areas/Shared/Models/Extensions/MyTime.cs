// File: Areas/Shared/Extensions/MyTime.cs
using System;

namespace JobPortal.Areas.Shared.Extensions
{
    /// <summary>
    /// Single-source-of-truth for Malaysia local time (MYT, UTC+8).
    /// Use NowMalaysia() for DB writes; ToMalaysiaTime(...) for display.
    /// </summary>
    public static class MyTime
    {
        private static readonly Lazy<TimeZoneInfo> MyTz = new Lazy<TimeZoneInfo>(() =>
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time"); }   // Windows
            catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kuala_Lumpur"); } // Linux/macOS
            catch (InvalidTimeZoneException) { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kuala_Lumpur"); }
        });

        /// <summary>Use this for all DB write timestamps that must be Malaysia local.</summary>
        public static DateTime NowMalaysia() =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, MyTz.Value);

        /// <summary>Convert a UTC (or Unspecified treated as UTC) time to Malaysia local time.</summary>
        public static DateTime ToMalaysiaTime(DateTime utcOrUnspecified)
        {
            var utc = utcOrUnspecified.Kind switch
            {
                DateTimeKind.Utc => utcOrUnspecified,
                DateTimeKind.Local => utcOrUnspecified.ToUniversalTime(),
                DateTimeKind.Unspecified => DateTime.SpecifyKind(utcOrUnspecified, DateTimeKind.Utc),
                _ => utcOrUnspecified
            };
            return TimeZoneInfo.ConvertTimeFromUtc(utc, MyTz.Value);
        }

        public static DateTime? ToMalaysiaTime(DateTime? utcOrUnspecified) =>
            utcOrUnspecified.HasValue ? ToMalaysiaTime(utcOrUnspecified.Value) : (DateTime?)null;
    }
}
