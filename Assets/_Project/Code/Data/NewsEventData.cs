using UnityEngine;

namespace RevManager {
    /// <summary>
    /// Journal color coding from the design doc: flavour text, important news,
    /// and crisis events that demand attention.
    /// </summary>
    public enum NewsTone {
        Flavor,
        Important,
        Crisis,
    }

    /// <summary>
    /// A breaking news / special event entry (Pandemic and Plague Inc style).
    /// These inform the player of the political climate and can push on any
    /// variable. Real or absurd; the design doc wants both.
    /// </summary>
    [CreateAssetMenu(menuName = "RevManager/News Event", fileName = "NewNewsEvent")]
    public class NewsEventData : ScriptableObject {
        [Header("Journal entry")]
        public string Headline;
        [TextArea(3, 8)] public string Body;
        [Tooltip("Optional image for the journal or VCR screen. Scavenged + credited.")]
        public Sprite Image;
        public NewsTone Tone = NewsTone.Flavor;

        [Header("Scheduling")]
        [Tooltip("Won't appear before this week (1 to 4). Lets the news escalate as the machine gets desperate.")]
        [Min(1)] public int EarliestWeek = 1;

        [Tooltip("Relative chance of being picked versus other eligible events.")]
        [Min(0f)] public float Weight = 1f;

        [Tooltip("If true, this event can only fire once per run.")]
        public bool OneTimeOnly = true;

        [Header("Impact")]
        [Tooltip("Applied the moment the event hits the journal.")]
        public VariableEffect[] EffectsOnFire;

        [Tooltip("Stretch goal from the doc: applied at week end if the player never engaged with this entry (missing news makes people leave).")]
        public VariableEffect[] EffectsIfIgnored;
    }
}
