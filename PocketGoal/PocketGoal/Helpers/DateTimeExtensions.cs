namespace PocketGoal.Helpers
{
    public static class DateTimeExtensions
    {
        /// <summary>
        /// Ensures a DateTime has Kind=Utc. For date inputs without timezone information (Unspecified),
        /// uses DateTime.SpecifyKind to label the existing clock value as UTC without shifting it.
        /// </summary>
        public static DateTime EnsureUtc(this DateTime dt) =>
            dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc);

        /// <summary>
        /// Ensures a nullable DateTime has Kind=Utc if it has a value.
        /// </summary>
        public static DateTime? EnsureUtc(this DateTime? dt) =>
            dt.HasValue ? dt.Value.EnsureUtc() : null;
    }
}
