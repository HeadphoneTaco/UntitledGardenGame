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
        [Tooltip("If true, this event can only fire once per run.")]
        public bool OneTimeOnly = true;

        [Header("Impact")]
        [Tooltip("Applied immediately when the news appears.")]
        public VariableEffect[] EffectsOnFire;

        [Header("Crisis Response")]
        [Tooltip("Resources consumed when the player chooses Attend.")]
        public VariableCost[] AttendCosts;
        [Tooltip("How many in-game hours the player has to respond.")]
        [Min(0.1f)]
        public float ResponseHours = 3f;

        [Tooltip("Applied after the player pays the cost and attends.")]
        public VariableEffect[] EffectsOnAttend;
        

        [Tooltip("Applied when the player chooses Ignore.")]
        public VariableEffect[] EffectsIfIgnored;
        
    }
}
