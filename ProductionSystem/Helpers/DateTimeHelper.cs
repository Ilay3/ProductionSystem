namespace ProductionSystem.Helpers
{
    public static class DateTimeHelper
    {
        public static DateTime SafeDateTime(this DateTime dateTime)
        {
            return DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified);
        }

        public static DateTime? SafeDateTime(this DateTime? dateTime)
        {
            if (!dateTime.HasValue) return null;
            return DateTime.SpecifyKind(dateTime.Value, DateTimeKind.Unspecified);
        }
    }
}
