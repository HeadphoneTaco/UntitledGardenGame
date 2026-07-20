using UnityEngine;

namespace RevManager {
    /// <summary>
    /// The three jobs an action can do, from the design doc:
    /// Care refills the Community bar, Grow adds people (more people = more
    /// action points per day), Fight drains the Machine bar.
    /// </summary>
    public enum ActionType {
        Community,
        Organize,
        Resist,
    }

    /// <summary>
    /// One queueable weekday action (farming, rally prep, remove propaganda...).
    /// All numbers are data so the design can change without code edits:
    /// point Effects at any variable (Community, Machine, Food, People) and
    /// set Costs for what it consumes.
    /// </summary>
    [CreateAssetMenu(menuName = "RevManager/Action", fileName = "NewAction")]
    public class ActionData : ScriptableObject {
        [Header("Presentation")]
        public string DisplayName;
        [TextArea] public string Description;
        [Tooltip("Scavenged + credited, like all art.")]
        public Sprite Icon;

        [Header("Gameplay")]
        public ActionType Type = ActionType.Community;

        [Header("Unlocking (tier tech tree)")]
        [Tooltip("Hidden until the commune reaches this tier. Tier 1 = available from the start.")]
        [Range(1, 4)] public int Tier = 1;

        [Tooltip("Completing this action raises the commune to this tier (0 = doesn't advance). E.g. Write Manifesto = 2, Establish Regional Communes = 3, Start the Revolution = 4.")]
        [Range(0, 4)] public int UnlocksTier;

        [Tooltip("Actions that must be completed this run before this one appears. E.g. Make Clothing and Pottery requires Build Garden; Tier 4 requires Mobile Medical Teams + General Strike + Train Militia Members.")]
        public ActionData[] Prerequisites;

        [Tooltip("Supporters (People) needed before this can be queued. A requirement, not a cost — shown in the detail card, nothing is spent. Tier gates: 15 / 40 / 100.")]
        [Min(0f)] public float MinSupporters;

        [Tooltip("Off = one-shot (builds, tier advances): the action disappears from the list after completing once this run.")]
        public bool Repeatable = true;

        [Tooltip("Action points this takes out of the day.")]
        [Min(1)] public int TimeCost = 1;

        [Tooltip("Must be affordable before the action can run; spent when it does.")]
        public VariableCost[] Costs;

        [Tooltip("Changes applied when the action completes.")]
        public VariableEffect[] Effects;

        public bool CanAfford => Costs.CanAffordAll();

        public void Execute() {
            Costs.PayAll();
            Effects.ApplyAll();
        }
    }
}
