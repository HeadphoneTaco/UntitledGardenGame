using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RevManager {
    /// <summary>
    /// Same idea as ActionButtonBinding but for the Friday choice. Assign a
    /// WeekendOptionData asset; the button labels itself, wires its own click,
    /// and is only clickable during the Weekend phase.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class WeekendButtonBinding : MonoBehaviour {
        [SerializeField] private WeekendOptionData m_Option;
        [SerializeField] private Button m_Button;
        [SerializeField] private TextMeshProUGUI m_Label;

        private void Reset() {
            FindParts();
        }

        private void OnValidate() {
            FindParts();
            UpdateLabel();
        }

        private void Awake() {
            FindParts();
            UpdateLabel();
            m_Button.onClick.AddListener(OnClick);
        }

        private void Update() {
            m_Button.interactable = RevGameManager.Exists && RevGameManager.Instance.CanChoose(m_Option);
        }

        private void OnClick() {
            if (RevGameManager.Exists) {
                RevGameManager.Instance.ChooseWeekend(m_Option);
            }
        }

        private void FindParts() {
            if (!m_Button) {
                m_Button = GetComponent<Button>();
            }
            if (!m_Label) {
                m_Label = GetComponentInChildren<TextMeshProUGUI>();
            }
        }

        private void UpdateLabel() {
            if (m_Label && m_Option) {
                m_Label.text = m_Option.DisplayName;
            }
        }
    }
}
