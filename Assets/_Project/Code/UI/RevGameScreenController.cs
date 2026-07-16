using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace RevManager {
    /// <summary>
    /// Binds RevGameScreen.uxml to the game. Generates action and weekend
    /// buttons from the buckets (new content assets appear automatically),
    /// updates bars and labels, feeds the journal, and shows the ending.
    ///
    /// Display updates poll on a 100ms schedule instead of subscribing to
    /// every variable: simpler, and plenty fast for a turn-based game.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class RevGameScreenController : MonoBehaviour {
        private ProgressBar m_CommunityBar;
        private ProgressBar m_MachineBar;
        private Label m_CommunityBarLabel;
        private Label m_MachineBarLabel;
        private Label m_ClockLabel;
        private Label m_PeopleLabel;
        private Label m_ApLabel;
        private VisualElement m_WeekendPanel;
        private VisualElement m_EndingOverlay;
        private Label m_EndingTitle;
        private Label m_EndingBody;
        private Button m_EndDayButton;
        private ScrollView m_JournalScroll;

        private readonly List<(Button button, ActionData action)> m_ActionButtons = new List<(Button, ActionData)>();
        private readonly List<(Button button, WeekendOptionData option)> m_WeekendButtons = new List<(Button, WeekendOptionData)>();

        private RevGameManager Manager => RevGameManager.Exists ? RevGameManager.Instance : null;
        private bool m_Built;

        private void OnEnable() {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;

            m_CommunityBar = root.Q<ProgressBar>("community-bar");
            m_MachineBar = root.Q<ProgressBar>("machine-bar");
            m_CommunityBarLabel = root.Q<Label>("community-bar-label");
            m_MachineBarLabel = root.Q<Label>("machine-bar-label");
            m_ClockLabel = root.Q<Label>("clock-label");
            m_PeopleLabel = root.Q<Label>("people-label");
            m_ApLabel = root.Q<Label>("ap-label");
            m_WeekendPanel = root.Q<VisualElement>("weekend-panel");
            m_EndingOverlay = root.Q<VisualElement>("ending-overlay");
            m_EndingTitle = root.Q<Label>("ending-title");
            m_EndingBody = root.Q<Label>("ending-body");
            m_EndDayButton = root.Q<Button>("end-day-button");
            m_JournalScroll = root.Q<ScrollView>("journal-scroll");

            m_EndDayButton.clicked += OnEndDayClicked;
            root.Q<Button>("restart-button").clicked += OnRestartClicked;

            // Manager may not exist yet (script order); build once it does.
            root.schedule.Execute(Refresh).Every(100);
        }

        private void OnDisable() {
            if (RevGameManager.Exists) {
                RevGameManager.Instance.JournalUpdated -= OnJournalUpdated;
                RevGameManager.Instance.GameEnded -= OnGameEnded;
            }
            m_Built = false;
        }

        // ---- One-time build from buckets ----

        private void BuildOnce() {
            if (m_Built || Manager == null) {
                return;
            }
            m_Built = true;

            Manager.JournalUpdated += OnJournalUpdated;
            Manager.GameEnded += OnGameEnded;

            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            var groups = new Dictionary<ActionType, VisualElement> {
                { ActionType.Care, root.Q<VisualElement>("care-group") },
                { ActionType.Grow, root.Q<VisualElement>("grow-group") },
                { ActionType.Fight, root.Q<VisualElement>("fight-group") },
            };

            if (Manager.Actions) {
                foreach (ActionData action in Manager.Actions.Items) {
                    ActionData captured = action;
                    var button = new Button(() => Manager.ExecuteAction(captured)) {
                        text = $"{action.DisplayName}  ({action.TimeCost} pt)",
                        tooltip = action.Description,
                    };
                    button.AddToClassList("action-button");
                    groups[action.Type].Add(button);
                    m_ActionButtons.Add((button, action));
                }
            }

            VisualElement weekendContainer = root.Q<VisualElement>("weekend-container");
            if (Manager.WeekendOptions) {
                foreach (WeekendOptionData option in Manager.WeekendOptions.Items) {
                    WeekendOptionData captured = option;
                    var button = new Button(() => Manager.ChooseWeekend(captured)) {
                        text = option.DisplayName,
                        tooltip = option.Description,
                    };
                    button.AddToClassList("weekend-button");
                    weekendContainer.Add(button);
                    m_WeekendButtons.Add((button, option));
                }
            }

            // Backfill journal entries that fired before the UI was ready.
            foreach (JournalEntry entry in Manager.Journal) {
                OnJournalUpdated(entry);
            }
        }

        // ---- Polled display refresh ----

        private void Refresh() {
            if (Manager == null) {
                return;
            }

            BuildOnce();

            m_CommunityBar.value = Manager.Community.Progress * 100f;
            m_CommunityBarLabel.text = $"Community  {Manager.Community.Progress * 100f:0}%";
            m_MachineBar.value = Manager.Machine.Progress * 100f;
            m_MachineBarLabel.text = $"The Machine  {Manager.Machine.Progress * 100f:0}%";

            m_ClockLabel.text = Manager.Phase == GamePhase.Weekend
                ? $"Week {Manager.Week.Value} - Weekend"
                : $"Week {Manager.Week.Value} - Day {Manager.Day.Value} of {Manager.DaysPerWeek}";
            m_PeopleLabel.text = $"People: {Manager.People.Value:0}";
            m_ApLabel.text = $"Action Points: {Manager.ActionPointsLeft.Value}";

            bool weekend = Manager.Phase == GamePhase.Weekend;
            m_WeekendPanel.style.display = weekend ? DisplayStyle.Flex : DisplayStyle.None;
            m_EndDayButton.SetEnabled(Manager.Phase == GamePhase.Weekday);

            foreach ((Button button, ActionData action) in m_ActionButtons) {
                button.SetEnabled(Manager.CanExecute(action));
            }
            foreach ((Button button, WeekendOptionData option) in m_WeekendButtons) {
                button.SetEnabled(Manager.CanChoose(option));
            }
        }

        // ---- Events ----

        private void OnEndDayClicked() {
            Manager?.EndDay();
        }

        private void OnRestartClicked() {
            m_JournalScroll.Clear();
            m_EndingOverlay.RemoveFromClassList("ending-overlay--visible");
            Manager?.StartRun();
        }

        private void OnJournalUpdated(JournalEntry entry) {
            var label = new Label($"W{entry.Week}D{entry.Day}  {entry.Headline}");
            label.AddToClassList("journal-entry");
            label.AddToClassList($"journal-entry--{entry.Tone.ToString().ToLowerInvariant()}");
            m_JournalScroll.Add(label);
            m_JournalScroll.schedule.Execute(() => m_JournalScroll.ScrollTo(label));
        }

        private void OnGameEnded(EndingData ending) {
            m_EndingTitle.text = ending ? ending.Title : "It's Over";
            m_EndingBody.text = ending ? ending.Body : "No ending matched. Check the EndingBucket has a fallback with open conditions.";
            m_EndingOverlay.AddToClassList("ending-overlay--visible");
        }
    }
}
