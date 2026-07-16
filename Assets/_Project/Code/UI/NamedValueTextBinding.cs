using CoreUtils.GameVariables;
using TMPro;
using UnityEngine;

namespace RevManager {
    /// <summary>
    /// Shows "Name: value" for any GameVariable (CoreUtils' ValueTextBinding
    /// shows only the value). Works for the Numbers panel: People, Week, Day,
    /// Action Points. Names come from the variable assets, so renaming an
    /// asset renames the UI.
    /// </summary>
    public class NamedValueTextBinding : MonoBehaviour {
        [SerializeField] private BaseGameVariable m_Variable;
        [SerializeField] private TextMeshProUGUI m_Label;

        private void Reset() {
            FindParts();
        }

        private void OnValidate() {
            FindParts();
            UpdateText();
        }

        private void OnEnable() {
            if (m_Variable) {
                m_Variable.GenericEvent += UpdateText;
            }
            UpdateText();
        }

        private void OnDisable() {
            if (m_Variable) {
                m_Variable.GenericEvent -= UpdateText;
            }
        }

        private void FindParts() {
            if (!m_Label) {
                m_Label = GetComponent<TextMeshProUGUI>();
            }
        }

        private void UpdateText() {
            if (m_Label && m_Variable) {
                m_Label.text = $"{m_Variable.Name}: {m_Variable.ValueString}";
            }
        }
    }
}
