using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace RevManager {
    /// <summary>
    /// One-time construction: the rows and buttons that only need building
    /// once, either on enable (resources, weekend options) or the first poll
    /// where the manager exists (action rows, journal backfill).
    ///
    /// See RevGameScreenController.cs for the layout map and field list.
    /// </summary>
    public partial class RevGameScreenController {
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
            Manager.ActionCompleted += OnActionCompleted;
            Manager.NewsFired += OnNewsFired;
            Manager.CrisisResolved += OnCrisisResolved;
            
            m_NewsTvClose.clicked += CloseNewsTv;
            m_NewsTvContinue.clicked += CloseNewsTv;
            m_NewsTvAttend.clicked += AttendNewsTvCrisis;
            m_NewsTvIgnore.clicked += IgnoreNewsTvCrisis;
            

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
                    RegisterButtonAudio(button);
                    button.AddToClassList("action-row");

                    var icon = new VisualElement();
                    icon.AddToClassList("action-row__icon");
                    if (action.Icon) {
                        icon.style.backgroundImage = new StyleBackground(action.Icon);
                    }
                    var name = new Label(action.DisplayName);
                    name.AddToClassList("action-row__name");
                    var time = new Label($"{action.TimeCost}mh");
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
    }
}
