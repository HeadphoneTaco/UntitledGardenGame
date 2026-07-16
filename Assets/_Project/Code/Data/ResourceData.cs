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

        [Tooltip("Scavenged + credited, like all art.")]
        public Sprite Icon;

        [Tooltip("The variable asset holding the current amount (create via CoreUtils/GameVariable/FloatRange).")]
        public GameVariableFloatRange Variable;
    }
}
