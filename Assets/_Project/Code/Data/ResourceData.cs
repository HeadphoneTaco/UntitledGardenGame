using CoreUtils.GameVariables;
using UnityEngine;

namespace RevManager {
    /// <summary>
    /// Presentation wrapper for a community resource (Food, Water, Health...).
    /// The actual number lives in a GameVariableFloatRange asset, which gives us
    /// clamping, change events, and free UI binding via CoreUtils SliderBinding.
    /// This wrapper adds the display info the resource list UI needs.
    /// </summary>
    [CreateAssetMenu(menuName = "RevManager/Resource", fileName = "NewResource")]
    public class ResourceData : ScriptableObject {
        public string DisplayName;
        [TextArea] public string Description;

        [Tooltip("Small icon (RevMan-Icon-*). Used in drain rows and cost/gain lines. Scavenged + credited, like all art.")]
        public Sprite Icon;

        [Tooltip("Full tile art for the bottom 'stuff' grid (RevMan-inventory-*). Falls back to Icon if empty.")]
        public Sprite Tile;

        [Tooltip("Badge text on the drain bar row, e.g. '-10/min'. Only shown for resources in the drain bucket. Leave empty to hide.")]
        public string DrainLabel;

        [Tooltip("Amount lost per in-game hour while the day clock is running (the clock only runs while the action queue is working). Only resources in the DrainResourceBucket drain. DrainLabel is display-only; this is the real number.")]
        [Min(0f)] public float DrainPerHour;

        [Tooltip("The variable asset holding the current amount (create via CoreUtils/GameVariable/FloatRange).")]
        public GameVariableFloatRange Variable;
    }
}
