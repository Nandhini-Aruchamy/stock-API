namespace stock_API.Utils
{
    public static class FilingPeriodHelper
    {
        // 13F-HR deadlines: Q1→May15, Q2→Aug15, Q3→Nov15, Q4→Feb15 (next year)
        private static readonly (int DeadlineMonth, int DeadlineDay, int DeadlineYearOffset)[] Deadlines =
        [
            (5,  15, 0),   // Q1
            (8,  15, 0),   // Q2
            (11, 15, 0),   // Q3
            (2,  15, 1),   // Q4 — deadline is in the following year
        ];

        /// <summary>
        /// Returns the most recent quarter whose 13F filing deadline has passed.
        /// e.g. on 2026-03-30 → (Year=2025, Quarter=4, Label="Q4 2025")
        /// </summary>
        public static (int Year, int Quarter, string Label) GetLatestFilingPeriod(DateOnly today)
        {
            // Check from most recent backward — Q4 prev year through Q4 this year
            var candidates = new (int DataYear, int Q)[]
            {
                (today.Year - 1, 4),
                (today.Year,     1),
                (today.Year,     2),
                (today.Year,     3),
                (today.Year,     4),
            };

            // Walk in reverse; return the first whose deadline ≤ today
            for (int i = candidates.Length - 1; i >= 0; i--)
            {
                var (dataYear, q) = candidates[i];
                var (dMonth, dDay, dYearOffset) = Deadlines[q - 1];
                var deadline = new DateOnly(dataYear + dYearOffset, dMonth, dDay);

                if (deadline <= today)
                    return (dataYear, q, $"Q{q} {dataYear}");
            }

            // Ultimate fallback
            return (today.Year - 2, 4, $"Q4 {today.Year - 2}");
        }
    }
}
