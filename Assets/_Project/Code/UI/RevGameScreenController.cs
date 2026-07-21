using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UIElements;

namespace RevManager {
    /// <summary>
    /// Binds RevGameScreen.uxml (the reference mockup layout) to the game.
    ///
    /// Layout map:
    ///   Top bar     - day counter, action points (time), Support/Community/Machine bars.
    ///   Left panel  - Community Resources: one bar row per resource (full
    ///                 height; draining ones carry a DrainLabel badge).
    ///   Center      - Available Actions grouped by category; clicking a row shows
    ///                 the detail card (costs/gains) with Add First/Last In Queue.
    ///   Right       - Daily Queue (actions taken today) + End Of Week Plan buttons.
    ///   Bottom      - "stuff" inventory grid + "news" journal feed.
    ///
    /// Queue: Add First/Add Last feed RevGameManager's timed queue. The front
    /// item's bar drains live; clicking any queued row cancels it (nothing is
    /// paid until an action completes, so no refund math). End Day only lights
    /// up while the queue is idle — with 0 hours left the day ends by itself.
    ///
    /// Display updates poll on a 100ms schedule instead of subscribing to
    /// every variable: simpler, and plenty fast at this scale.
    ///
    /// Split across partial files, one per concern:
    ///   RevGameScreenController.cs          - fields, lifecycle, the polled Refresh
    ///   RevGameScreenController.Build.cs    - one-time construction of rows/buttons
    ///   RevGameScreenController.Actions.cs  - center panel selection + detail card
    ///   RevGameScreenController.Queue.cs    - right panel queue display + queue events
    ///   RevGameScreenController.Journal.cs  - news feed, ending overlay, button audio
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public partial class RevGameScreenController : MonoBehaviour {
        [Header("Resources (auto-collects from its source folder)")]
        [SerializeField, FormerlySerializedAs("m_Inventory")]
        [Tooltip("Every resource shows as a bar row in the Community Resources panel. Source folder: ScriptableObjects/Resources (includes Draining).")]
        private ResourceBucket m_Resources;

        [Header("End of week plan (weekend options, top to bottom)")]
        [SerializeField] private WeekendOptionData m_RestOption;
        [SerializeField] private WeekendOptionData m_LocalOption;
        [SerializeField] private WeekendOptionData m_BigOption;

        [Header("Support bar cap (People has no max; bar fills toward this)")]
        [SerializeField, Min(1f)] private float m_SupportBarCap = 100f;
        
        [Header("News TV Backgrounds")]
        [SerializeField] private Texture2D[] m_NewsTvBackgrounds;
        
        [SerializeField] private Texture2D m_NewsTvFrameArt;
        [SerializeField] private Texture2D m_NewsTvImportantContainer;
        [SerializeField] private Texture2D m_NewsTvCrisisContainer;
        [SerializeField] private Texture2D m_NewsTvAttendArt;
        [SerializeField] private Texture2D m_NewsTvIgnoreArt;

        private VisualElement m_NewsTvFrame;
        private VisualElement m_NewsTvPaper;

        private readonly Dictionary<NewsEventData, Texture2D> m_NewsTvBackgroundByStory = new();

        // Top bar
        private Label m_DayLabel;
        private Label m_ApLabel;
        private Label m_SupportValue;
        private Label m_CommunityValue;
        private Label m_MachineValue;
        private VisualElement m_SupportFill;
        private VisualElement m_CommunityFill;
        private VisualElement m_MachineFill;

        // Left panel
        private class ResourceRow {
            public VisualElement Fill;
            public Label Value;
            public ResourceData Resource;
            public float Max; // Derived from Value/Progress (package hides MaxValue); cached once valid.
        }

        private readonly List<ResourceRow> m_ResourceRows = new List<ResourceRow>();

        // Center panel
        private readonly List<(Button button, ActionData action)> m_ActionButtons = new List<(Button, ActionData)>();
        private ActionData m_Selected;
        private VisualElement m_DetailIcon;
        private Label m_DetailName;
        private Label m_DetailDesc;
        private VisualElement m_DetailCosts;
        private VisualElement m_DetailGains;
        private Label m_DetailStatus;
        private Button m_AddFirstButton;
        private Button m_AddLastButton;
        // Cost lines of the selected action, re-checked every poll so lines
        // turn red the moment a resource drains below the price.
        private readonly List<(Label line, VariableCost cost)> m_SelectedCostLines = new List<(Label, VariableCost)>();

        // Right panel
        private ScrollView m_QueueList;
        private Button m_EndDayButton;
        private readonly List<(Button button, WeekendOptionData option)> m_WeekendButtons = new List<(Button, WeekendOptionData)>();
        private int m_ShownQueueVersion = -1;
        private VisualElement m_ActiveQueueFill; // Front item's bar; drains every poll without a rebuild.

        // Bottom
        private ScrollView m_JournalScroll;
        
        // News TV
        private VisualElement m_NewsTvOverlay;
        private VisualElement m_NewsTvBackground;
        private Label m_NewsTvHeadline;
        private Label m_NewsTvBody;
        private VisualElement m_NewsTvCrisisArea;
        private VisualElement m_NewsTvTimerRing;
        private VisualElement m_NewsTvCosts;
        private Button m_NewsTvClose;
        private Button m_NewsTvContinue;
        private Button m_NewsTvIgnore;
        private Button m_NewsTvAttend;
        private NewsEventData m_DisplayedNews;
        private Label m_NewsTvTimerText;
        private float m_NewsTvTimerProgress = 1f;
        
        private IVisualElementScheduledItem m_NewsTvAutoClose;

        // Ending
        private VisualElement m_EndingOverlay;
        private Label m_EndingTitle;
        private Label m_EndingBody;

        private RevGameManager Manager => RevGameManager.Exists ? RevGameManager.Instance : null;
        private bool m_Built;

        private void OnEnable() {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            root.Query<Button>().ForEach(RegisterButtonAudio);

            m_DayLabel = root.Q<Label>("day-label");
            m_ApLabel = root.Q<Label>("ap-label");
            m_SupportValue = root.Q<Label>("support-value");
            m_CommunityValue = root.Q<Label>("community-value");
            m_MachineValue = root.Q<Label>("machine-value");
            m_SupportFill = root.Q<VisualElement>("support-fill");
            m_CommunityFill = root.Q<VisualElement>("community-fill");
            m_MachineFill = root.Q<VisualElement>("machine-fill");

            BuildResourceRows(root.Q<ScrollView>("drain-list"));

            m_DetailIcon = root.Q<VisualElement>("detail-icon");
            m_DetailName = root.Q<Label>("detail-name");
            m_DetailDesc = root.Q<Label>("detail-desc");
            m_DetailCosts = root.Q<VisualElement>("detail-costs");
            m_DetailGains = root.Q<VisualElement>("detail-gains");
            m_DetailStatus = root.Q<Label>("detail-status");
            m_AddFirstButton = root.Q<Button>("add-first-button");
            m_AddLastButton = root.Q<Button>("add-last-button");
            m_AddFirstButton.clicked += () => OnAddToQueueClicked(true);
            m_AddLastButton.clicked += () => OnAddToQueueClicked(false);

            m_QueueList = root.Q<ScrollView>("queue-list");
            m_EndDayButton = root.Q<Button>("end-day-button");
            m_EndDayButton.clicked += OnEndDayClicked;

            BuildWeekendButton(root, "eow-rest-button", m_RestOption);
            BuildWeekendButton(root, "eow-local-button", m_LocalOption);
            BuildWeekendButton(root, "eow-big-button", m_BigOption);

            m_JournalScroll = root.Q<ScrollView>("journal-scroll");
            
            m_NewsTvOverlay = root.Q<VisualElement>("news-tv-overlay");
            m_NewsTvBackground = root.Q<VisualElement>("news-tv-background");
            m_NewsTvFrame = root.Q<VisualElement>("news-tv-frame");
            m_NewsTvPaper = root.Q<VisualElement>("news-tv-paper");

            if (m_NewsTvFrameArt) m_NewsTvFrame.style.backgroundImage = new StyleBackground(m_NewsTvFrameArt);
            
            m_NewsTvHeadline = root.Q<Label>("news-tv-headline");
            m_NewsTvBody = root.Q<Label>("news-tv-body");
            m_NewsTvCrisisArea = root.Q<VisualElement>("news-tv-crisis-area");
            m_NewsTvTimerRing = root.Q<VisualElement>("news-tv-timer-ring");
            m_NewsTvTimerRing.generateVisualContent += DrawCrisisTimerRing;
            
            m_NewsTvTimerText = root.Q<Label>("news-tv-timer-text");
            m_NewsTvCosts = root.Q<VisualElement>("news-tv-costs");
            m_NewsTvClose = root.Q<Button>("news-tv-close");
            m_NewsTvContinue = root.Q<Button>("news-tv-continue");
            m_NewsTvIgnore = root.Q<Button>("news-tv-ignore");
            m_NewsTvAttend = root.Q<Button>("news-tv-attend");
            if (m_NewsTvAttendArt) m_NewsTvAttend.style.backgroundImage = new StyleBackground(m_NewsTvAttendArt);
            if (m_NewsTvIgnoreArt) m_NewsTvIgnore.style.backgroundImage = new StyleBackground(m_NewsTvIgnoreArt);

            m_EndingOverlay = root.Q<VisualElement>("ending-overlay");
            m_EndingTitle = root.Q<Label>("ending-title");
            m_EndingBody = root.Q<Label>("ending-body");
            root.Q<Button>("restart-button").clicked += OnRestartClicked;

            // Manager may not exist yet (script order); build once it does.
            root.schedule.Execute(Refresh).Every(100);
        }

        private void OnDisable() {
            if (RevGameManager.Exists) {
                RevGameManager.Instance.JournalUpdated -= OnJournalUpdated;
                RevGameManager.Instance.GameEnded -= OnGameEnded;
                RevGameManager.Instance.ActionCompleted -= OnActionCompleted;
                RevGameManager.Instance.NewsFired -= OnNewsFired;
                RevGameManager.Instance.CrisisResolved -= OnCrisisResolved;
                
                m_NewsTvClose.clicked -= CloseNewsTv;
                m_NewsTvContinue.clicked -= CloseNewsTv;
                m_NewsTvAttend.clicked -= AttendNewsTvCrisis;
                m_NewsTvIgnore.clicked -= IgnoreNewsTvCrisis;
                
                if (m_NewsTvTimerRing != null) m_NewsTvTimerRing.generateVisualContent -= DrawCrisisTimerRing;
                
                
            }
            m_Built = false;
        }

        // ---- Polled display refresh ----

        private void Refresh() {
            if (Manager == null) {
                return;
            }

            BuildOnce();

            // Top bar. Day counter runs across the whole campaign, mockup-style 00/00.
            int dayOverall = (Manager.Week.Value - 1) * Manager.DaysPerWeek + Manager.Day.Value;
            int dayTotal = Manager.TotalWeeks * Manager.DaysPerWeek;
            m_DayLabel.text = $"{dayOverall:00}/{dayTotal:00}";
            // Unreserved man-hours: what's still plannable. Costs settle on
            // completion, so raw ActionPointsLeft wouldn't move when you queue.
            m_ApLabel.text = $"{Manager.UnreservedHours}mh";

            float support = Manager.People.Value;
            m_SupportValue.text = $"{support:000}/{m_SupportBarCap:000}";
            SetFill(m_SupportFill, support / m_SupportBarCap);

            m_CommunityValue.text = $"{Manager.Community.Progress * 100f:000}/100";
            SetFill(m_CommunityFill, Manager.Community.Progress);

            m_MachineValue.text = $"{Manager.Machine.Progress * 100f:000}/100";
            SetFill(m_MachineFill, Manager.Machine.Progress);

            // Left panel resource bars.
            foreach (ResourceRow rowBinding in m_ResourceRows) {
                float current = rowBinding.Resource.Variable.Value;
                float progress = rowBinding.Resource.Variable.Progress;

                // Package hides MaxValue, so derive it (resources are 0-based)
                // and cache the first valid result.
                if (rowBinding.Max <= 0f && progress > 0.0001f) {
                    rowBinding.Max = Mathf.Round(current / progress);
                }

                rowBinding.Value.text = rowBinding.Max > 0f
                    ? $"{current:000}/{rowBinding.Max:000}"
                    : $"{current:000}";
                SetFill(rowBinding.Fill, progress);
            }

            // Center panel. Tier-locked and finished one-shots are hidden (the
            // tree reveals itself); prereq-locked rows show greyed so the
            // player knows something is there to unlock.
            // Rows stay CLICKABLE while blocked, so the player can select one
            // and read the reason in the detail card; the class just greys it.
            foreach ((Button button, ActionData action) in m_ActionButtons) {
                bool visible = Manager.IsVisible(action);
                button.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                bool blocked = Manager.Phase != GamePhase.Weekday
                    || !Manager.PrerequisitesMet(action) || !action.CanAfford;
                button.EnableInClassList("action-row--blocked", blocked);
            }
            bool canQueue = Manager.CanQueue(m_Selected);
            m_AddFirstButton.SetEnabled(canQueue);
            m_AddLastButton.SetEnabled(canQueue);

            // Detail card feedback: say WHY the buttons are dead, and flush
            // the specific cost lines red that the commune can't cover.
            string blockReason = BlockReason(m_Selected);
            m_DetailStatus.text = blockReason ?? "";
            m_DetailStatus.style.display = blockReason != null ? DisplayStyle.Flex : DisplayStyle.None;
            foreach ((Label line, VariableCost cost) in m_SelectedCostLines) {
                line.EnableInClassList("detail-line--missing", !cost.CanAfford);
            }

            // Right panel.
            RefreshQueue();
            m_EndDayButton.SetEnabled(Manager.Phase == GamePhase.Weekday && Manager.Queue.Count == 0);
            foreach ((Button button, WeekendOptionData option) in m_WeekendButtons) {
                button.SetEnabled(Manager.CanChoose(option));
            }
            
            if (m_DisplayedNews &&
                Manager.PendingCrisis == m_DisplayedNews)
            {
                float hoursRemaining = Manager.CrisisHoursRemaining;
                float totalHours = Mathf.Max(1f, m_DisplayedNews.ResponseHours);

                m_NewsTvTimerText.text =
                    $"{Mathf.CeilToInt(hoursRemaining)} HOURS";

                float newProgress =
                    Mathf.Clamp01(hoursRemaining / totalHours);

                if (!Mathf.Approximately(
                        newProgress,
                        m_NewsTvTimerProgress))
                {
                    m_NewsTvTimerProgress = newProgress;
                    m_NewsTvTimerRing.MarkDirtyRepaint();
                }

                m_NewsTvAttend.SetEnabled(
                    Manager.CanAttendPendingCrisis);
            }

        }

        private static void SetFill(VisualElement fill, float progress) {
            fill.style.width = Length.Percent(Mathf.Clamp01(progress) * 100f);
        }
    }

}
