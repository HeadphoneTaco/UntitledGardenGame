using System;
using System.Collections.Generic;
using CoreUtils.GameVariables;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UIElements;

namespace RevManager {
    /// <summary>
    /// Binds RevGameScreen.uxml (Du's mockup layout) to the game.
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
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class RevGameScreenController : MonoBehaviour {
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
        private Button m_AddFirstButton;
        private Button m_AddLastButton;

        // Right panel
        private ScrollView m_QueueList;
        private Button m_EndDayButton;
        private readonly List<(Button button, WeekendOptionData option)> m_WeekendButtons = new List<(Button, WeekendOptionData)>();
        private int m_ShownQueueVersion = -1;
        private VisualElement m_ActiveQueueFill; // Front item's bar; drains every poll without a rebuild.

        // Bottom
        private ScrollView m_JournalScroll;

        // Ending
        private VisualElement m_EndingOverlay;
        private Label m_EndingTitle;
        private Label m_EndingBody;

        private RevGameManager Manager => RevGameManager.Exists ? RevGameManager.Instance : null;
        private bool m_Built;

        private void OnEnable() {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;

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
            }
            m_Built = false;
        }

        // ---- One-time build ----

        /// <summary>One bar row per resource, so every stat reads the same way.
        /// Rows come from the bucket: new ResourceData assets appear automatically.</summary>
        private void BuildResourceRows(ScrollView container) {
            if (!m_Resources) {
                return;
            }

            foreach (ResourceData resource in m_Resources.Items) {
                if (!resource || !resource.Variable) {
                    continue;
                }

                var row = new VisualElement();
                row.AddToClassList("drain-row");

                var icon = new VisualElement();
                icon.AddToClassList("drain-row__icon");
                if (resource.Icon) {
                    icon.style.backgroundImage = new StyleBackground(resource.Icon);
                }

                // Name + count on one line, full-width bar underneath, so every
                // bar is the same length no matter how long the name is.
                var col = new VisualElement();
                col.AddToClassList("drain-row__col");

                var head = new VisualElement();
                head.AddToClassList("drain-row__head");
                var name = new Label(resource.DisplayName);
                name.AddToClassList("drain-row__name");
                var value = new Label("000/000");
                value.AddToClassList("drain-row__value");
                head.Add(name);
                head.Add(value);

                var bar = new VisualElement();
                bar.AddToClassList("fill-bar");
                bar.AddToClassList("drain-row__bar");
                var fill = new VisualElement();
                fill.AddToClassList("fill-bar__fill");
                bar.Add(fill);

                col.Add(head);
                col.Add(bar);

                row.Add(icon);
                row.Add(col);

                if (!string.IsNullOrEmpty(resource.DrainLabel)) {
                    var badge = new Label(resource.DrainLabel);
                    badge.AddToClassList("drain-row__badge");
                    row.Add(badge);
                }

                container.Add(row);
                m_ResourceRows.Add(new ResourceRow { Fill = fill, Value = value, Resource = resource });
            }
        }

        private void BuildWeekendButton(VisualElement root, string elementName, WeekendOptionData option) {
            Button button = root.Q<Button>(elementName);
            if (button == null) {
                return;
            }
            if (!option) {
                button.style.display = DisplayStyle.None;
                return;
            }
            button.tooltip = option.Description;
            button.clicked += () => Manager?.ChooseWeekend(option);
            m_WeekendButtons.Add((button, option));
        }

        /// <summary>Action rows come from the bucket, so new ActionData assets appear automatically.</summary>
        private void BuildOnce() {
            if (m_Built || Manager == null) {
                return;
            }
            m_Built = true;

            Manager.JournalUpdated += OnJournalUpdated;
            Manager.GameEnded += OnGameEnded;

            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            var groups = new Dictionary<ActionType, VisualElement> {
                { ActionType.Community, root.Q<VisualElement>("care-group") },
                { ActionType.Organize, root.Q<VisualElement>("grow-group") },
                { ActionType.Resist, root.Q<VisualElement>("fight-group") },
            };

            if (Manager.Actions) {
                foreach (ActionData action in Manager.Actions.Items) {
                    if (!action) {
                        continue;
                    }
                    ActionData captured = action;
                    var button = new Button(() => SelectAction(captured));
                    button.AddToClassList("action-row");

                    var icon = new VisualElement();
                    icon.AddToClassList("action-row__icon");
                    if (action.Icon) {
                        icon.style.backgroundImage = new StyleBackground(action.Icon);
                    }
                    var name = new Label(action.DisplayName);
                    name.AddToClassList("action-row__name");
                    var time = new Label($"{action.TimeCost}h");
                    time.AddToClassList("action-row__time");

                    button.Add(icon);
                    button.Add(name);
                    button.Add(time);

                    groups[action.Type].Add(button);
                    m_ActionButtons.Add((button, action));

                    if (!m_Selected && Manager.IsVisible(action)) {
                        SelectAction(action);
                    }
                }
            }

            // Backfill journal entries that fired before the UI was ready.
            foreach (JournalEntry entry in Manager.Journal) {
                OnJournalUpdated(entry);
            }
        }

        // ---- Selection + detail card ----

        private void SelectAction(ActionData action) {
            m_Selected = action;

            foreach ((Button button, ActionData a) in m_ActionButtons) {
                button.EnableInClassList("action-row--selected", a == action);
            }

            m_DetailIcon.style.backgroundImage = action && action.Icon ? new StyleBackground(action.Icon) : new StyleBackground();
            m_DetailName.text = action ? action.DisplayName : "Select an action";
            m_DetailDesc.text = action ? action.Description : "";

            m_DetailCosts.Clear();
            m_DetailGains.Clear();
            if (!action) {
                return;
            }

            AddDetailLine(m_DetailCosts, $"{action.TimeCost} HOURS");
            if (action.MinSupporters > 0f) {
                // A requirement, not a cost: nothing is spent, but it gives
                // tier actions a visible goal to push toward.
                AddDetailLine(m_DetailCosts, $"NEEDS {action.MinSupporters:0} SUPPORTERS");
            }
            if (action.Costs != null) {
                foreach (VariableCost cost in action.Costs) {
                    if (cost.Variable) {
                        AddDetailLine(m_DetailCosts, $"x{cost.Amount:0} {cost.Variable.Name.ToUpperInvariant()}");
                    }
                }
            }

            if (action.Effects != null) {
                foreach (VariableEffect effect in action.Effects) {
                    if (effect.Variable) {
                        AddDetailLine(m_DetailGains, $"{effect.Delta:+0;-0} {effect.Variable.Name.ToUpperInvariant()}");
                    }
                }
            }
        }

        private static void AddDetailLine(VisualElement container, string text) {
            var line = new Label(text);
            line.AddToClassList("detail-line");
            container.Add(line);
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
            // Unreserved hours: what's still plannable. Costs settle on completion,
            // so raw ActionPointsLeft wouldn't move when you queue — confusing.
            m_ApLabel.text = $"{Manager.UnreservedHours}h";

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

            // Center panel. Locked/completed-one-shot actions are hidden
            // entirely (Du's call) — the list grows as tiers unlock.
            foreach ((Button button, ActionData action) in m_ActionButtons) {
                bool visible = Manager.IsVisible(action);
                button.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                button.SetEnabled(visible && Manager.Phase == GamePhase.Weekday && action.CanAfford);
            }
            bool canQueue = Manager.CanQueue(m_Selected);
            m_AddFirstButton.SetEnabled(canQueue);
            m_AddLastButton.SetEnabled(canQueue);

            // Right panel.
            RefreshQueue();
            m_EndDayButton.SetEnabled(Manager.Phase == GamePhase.Weekday && Manager.Queue.Count == 0);
            foreach ((Button button, WeekendOptionData option) in m_WeekendButtons) {
                button.SetEnabled(Manager.CanChoose(option));
            }

        }

        private static void SetFill(VisualElement fill, float progress) {
            fill.style.width = Length.Percent(Mathf.Clamp01(progress) * 100f);
        }

        /// <summary>
        /// Shows the PENDING queue (front item on top, bar draining as it works).
        /// Rows rebuild only when the manager bumps QueueVersion; between
        /// rebuilds each poll just updates the front bar's width.
        /// Clicking a row cancels it — nothing's paid until completion, so a
        /// cancel is free.
        /// </summary>
        private void RefreshQueue() {
            if (Manager.QueueVersion != m_ShownQueueVersion) {
                m_ShownQueueVersion = Manager.QueueVersion;
                m_QueueList.Clear();
                m_ActiveQueueFill = null;

                IReadOnlyList<QueuedAction> queue = Manager.Queue;
                for (int i = 0; i < queue.Count; i++) {
                    QueuedAction entry = queue[i];
                    ActionData action = entry.Action;
                    int index = i;

                    var row = new VisualElement();
                    row.AddToClassList("queue-row");
                    row.tooltip = "Click to cancel";
                    row.RegisterCallback<ClickEvent>(_ => OnQueueRowClicked(index, action));

                    var icon = new VisualElement();
                    icon.AddToClassList("queue-row__icon");
                    if (action.Icon) {
                        icon.style.backgroundImage = new StyleBackground(action.Icon);
                    }

                    var col = new VisualElement();
                    col.AddToClassList("queue-row__col");
                    var name = new Label($"{action.DisplayName}  ({action.TimeCost}h)");
                    name.AddToClassList("queue-row__name");
                    var bar = new VisualElement();
                    bar.AddToClassList("fill-bar");
                    var fill = new VisualElement();
                    fill.AddToClassList("fill-bar__fill");
                    // Paused (preempted) entries keep their partial bar; fresh ones show full.
                    fill.style.width = Length.Percent((1f - entry.Progress) * 100f);
                    bar.Add(fill);
                    col.Add(name);
                    col.Add(bar);

                    row.Add(icon);
                    row.Add(col);
                    m_QueueList.Add(row);

                    if (i == 0) {
                        m_ActiveQueueFill = fill;
                    }
                }
            }

            // Live drain on the working item: full bar -> empty as its hours run out.
            if (m_ActiveQueueFill != null) {
                m_ActiveQueueFill.style.width = Length.Percent((1f - Manager.ActiveProgress) * 100f);
            }
        }

        // ---- Events ----

        private void OnAddToQueueClicked(bool first) {
            if (m_Selected) {
                Manager?.TryEnqueue(m_Selected, first);
            }
        }

        private void OnQueueRowClicked(int index, ActionData action) {
            // Index was captured at build time; the manager double-checks it
            // against the action so a stale click can't cancel the wrong row.
            Manager?.TryCancelQueued(index, action);
        }

        private void OnEndDayClicked() {
            Manager?.EndDay();
        }

        private void OnRestartClicked() {
            m_JournalScroll.Clear();
            m_ShownQueueVersion = -1;
            m_EndingOverlay.RemoveFromClassList("ending-overlay--visible");
            Manager?.StartRun();
        }

        /// <summary>
        /// One journal entry = headline + collapsed body. Clicking a headline
        /// toggles the body; if the news carries an UrgentAction it ALSO pulls
        /// that action into the detail card, ready for Add First (which
        /// preempts whatever's running).
        /// Inline styles on urgent/body are placeholders until Du styles the
        /// classes (news-entry--actionable, news-entry__body) in the uss.
        /// </summary>
        private void OnJournalUpdated(JournalEntry entry) {
            // The container wears the newsprint strip (.news-entry) so the
            // paper stretches around the body when it expands; headline and
            // body inherit its color/font.
            var container = new VisualElement();
            container.AddToClassList("news-entry");
            container.AddToClassList($"news-entry--{entry.Tone.ToString().ToLowerInvariant()}");

            var label = new Label($"W{entry.Week}D{entry.Day}  {entry.Headline}");
            label.AddToClassList("news-entry__headline");
            container.Add(label);

            Label body = null;
            string bodyText = entry.Source ? entry.Source.Body : null;
            if (!string.IsNullOrEmpty(bodyText)) {
                body = new Label(bodyText);
                body.AddToClassList("news-entry__body");
                body.style.display = DisplayStyle.None;
                body.style.whiteSpace = WhiteSpace.Normal;
                body.style.opacity = 0.85f;
                body.style.marginTop = 4;
                container.Add(body);
            }

            ActionData urgent = entry.Source ? entry.Source.UrgentAction : null;
            if (urgent) {
                container.AddToClassList("news-entry--actionable");
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                container.tooltip = $"Click to plan: {urgent.DisplayName}";
            }

            if (urgent || body != null) {
                bool expanded = false;
                Label capturedBody = body;
                container.RegisterCallback<ClickEvent>(_ => {
                    if (capturedBody != null) {
                        expanded = !expanded;
                        capturedBody.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
                    }
                    if (urgent) {
                        SelectAction(urgent);
                    }
                });
            }

            m_JournalScroll.Add(container);
            m_JournalScroll.schedule.Execute(() => m_JournalScroll.ScrollTo(container));
        }

        private void OnGameEnded(EndingData ending) {
            m_EndingTitle.text = ending ? ending.Title : "It's Over";
            m_EndingBody.text = ending ? ending.Body : "No ending matched. Check the EndingBucket has a fallback with open conditions.";
            m_EndingOverlay.AddToClassList("ending-overlay--visible");
        }
    }
}
