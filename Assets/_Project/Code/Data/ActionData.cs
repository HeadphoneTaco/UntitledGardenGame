using UnityEngine;

namespace RevManager {
    /// <summary>
    /// The three jobs an action can do, from the design doc:
    /// Care refills the Community bar, Grow adds people (more people = more
    /// action points per day), Fight drains the Machine bar.
    /// </summary>
    public enum ActionType {
        Care,
        Grow,
        Fight,
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
        public ActionType Type = ActionType.Care;

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
