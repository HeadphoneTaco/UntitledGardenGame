namespace RevManager {
    /// <summary>
    /// One line in the side journal. UI colors these by Tone.
    /// </summary>
    public readonly struct JournalEntry {
        public readonly int Week;
        public readonly int Day;
        public readonly string Headline;
        public readonly NewsTone Tone;
        public readonly NewsEventData Source; // null for system messages (protest results, etc.)

        public JournalEntry(int week, int day, string headline, NewsTone tone, NewsEventData source = null) {
            Week = week;
            Day = day;
            Headline = headline;
            Tone = tone;
            Source = source;
        }
    }
}
