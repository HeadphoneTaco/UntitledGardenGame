using UnityEngine;

namespace RevManager {
    /// <summary>
    /// One of the Friday choices: rest, small action, or big mobilization.
    /// Core theme rule lives here: sending exhausted, hungry people into a big
    /// protest fails. MinCommunityProgress is that threshold.
    /// </summary>
    [CreateAssetMenu(menuName = "RevManager/Weekend Option", fileName = "NewWeekendOption")]
    public class WeekendOptionData : ScriptableObject {
        [Header("Presentation")]
        public string DisplayName;
        [TextArea] public string Description;

        [Header("Requirements")]
        [Tooltip("Community bar (0 to 1) required for this to succeed. Rest = 0. Big mobilization should be high.")]
        [Range(0f, 1f)] public float MinCommunityProgress;

        [Tooltip("Spent when chosen, success or not (people show up either way).")]
        public VariableCost[] Costs;

        [Header("Outcome")]
        [Tooltip("Damage to the Machine bar on success.")]
        [Min(0f)] public float MachineDamage;

        public VariableEffect[] EffectsOnSuccess;

        [Tooltip("What happens when the community was too exhausted and it goes wrong.")]
        public VariableEffect[] EffectsOnFailure;

        [Tooltip("Scales the machine's counterattack going into next week. Rest = 0, small = 1, big = 2+.")]
        [Min(0f)] public float RetaliationMultiplier = 1f;

        [Header("VCR payoff screen")]
        [Tooltip("Stolen footage stills / collage frames shown when this resolves. Credited per source.")]
        public Sprite[] CollageFrames;
    }
}
