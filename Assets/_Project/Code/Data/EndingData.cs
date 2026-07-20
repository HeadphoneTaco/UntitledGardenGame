using UnityEngine;

namespace RevManager {
    /// <summary>
    /// One ending from the doc's ladder: crushed movement, symbolic protest,
    /// occupied territory, liberated municipality, mass uprising, revolution.
    /// After the week 4 weekend (or an early collapse) the manager picks the
    /// highest-priority ending whose conditions match the two bars.
    /// </summary>
    [CreateAssetMenu(menuName = "RevManager/Ending", fileName = "NewEnding")]
    public class EndingData : ScriptableObject {
        [Header("Presentation")]
        public string Title;
        [TextArea(3, 10)] public string Body;
        [Tooltip("Scavenged + credited, like all art.")]
        public Sprite Image;

        [Tooltip("Ticked = the end screen uses the win art for this ending; otherwise the lose art.")]
        public bool IsVictory;

        [Header("Conditions (checked against the bars at game end)")]
        [Tooltip("Machine bar (0 to 1) must be AT OR BELOW this. Lower = requires more damage done.")]
        [Range(0f, 1f)] public float MaxMachineProgress = 1f;

        [Tooltip("Community bar (0 to 1) must be AT OR ABOVE this.")]
        [Range(0f, 1f)] public float MinCommunityProgress;

        [Header("Diagonal condition (lead = community - machine, in progress units)")]
        [Tooltip("Community lead must be AT OR ABOVE this. 0 = community must at least match the machine; -1 = no requirement. Lets endings cut along the diagonal instead of only in rectangles.")]
        [Range(-1f, 1f)] public float MinCommunityLead = -1f;

        [Tooltip("Community lead must be AT OR BELOW this. Pair with MinCommunityLead for a diagonal band (a stalemate). 1 = no requirement.")]
        [Range(-1f, 1f)] public float MaxCommunityLead = 1f;

        [Tooltip("Higher priority endings are checked first. Give the fallback ending priority 0 and open conditions.")]
        public int Priority;

        [Tooltip("If true, this ending is used when the community collapses before week 4.")]
        public bool IsEarlyCollapse;

        public bool Matches(float machineProgress, float communityProgress) {
            float lead = communityProgress - machineProgress;
            return machineProgress <= MaxMachineProgress
                && communityProgress >= MinCommunityProgress
                && lead >= MinCommunityLead
                && lead <= MaxCommunityLead;
        }
    }
}
