using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RevManager {
    /// <summary>
    /// Drop on a Button, assign an ActionData asset, done. The button labels
    /// itself from the action's DisplayName, wires its own click to the
    /// manager, and greys out when the action can't run (no points / can't
    /// afford / not a weekday). Do NOT also wire OnClick manually or the
    /// action fires twice.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class ActionButtonBinding : MonoBehaviour {
        [SerializeField] private ActionData m_Action;
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
            // Legacy uGUI binding (superseded by RevGameScreenController); kept compiling against the queue API.
            m_Button.interactable = RevGameManager.Exists && RevGameManager.Instance.CanQueue(m_Action);
        }

        private void OnClick() {
            if (RevGameManager.Exists) {
                RevGameManager.Instance.ExecuteAction(m_Action);
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
            if (m_Label && m_Action) {
                m_Label.text = m_Action.DisplayName;
            }
        }
    }
}
