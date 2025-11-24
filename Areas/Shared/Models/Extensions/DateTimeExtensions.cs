using System;

namespace JobPortal.Areas.Shared.Extensions
{
    public static class DateTimeExtensions
    {
        private static readonly TimeZoneInfo MalaysiaTz =
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Kuala_Lumpur");

        public static DateTime AsUtc(this DateTime value)
        {
            // EF from MySQL datetime usually comes as Unspecified
            return value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
                _ => value
            };
        }

        public static DateTime ToMalaysiaTime(this DateTime utc)
        {
            utc = utc.AsUtc();
            return TimeZoneInfo.ConvertTimeFromUtc(utc, MalaysiaTz);
        }

        public static DateTime? ToMalaysiaTime(this DateTime? utc)
        {
            return utc.HasValue ? utc.Value.ToMalaysiaTime() : (DateTime?)null;
        }
    }
}
