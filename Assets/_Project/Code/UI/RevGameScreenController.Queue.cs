using System.Collections.Generic;
using UnityEngine.UIElements;

namespace RevManager {
    /// <summary>
    /// Right panel: the pending queue display and the buttons that mutate it.
    ///
    /// See RevGameScreenController.cs for the layout map and field list.
    /// </summary>
    public partial class RevGameScreenController {
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
                    var name = new Label($"{action.DisplayName}  ({action.TimeCost}mh)");
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

        private void OnAddToQueueClicked(bool first)
        {
            if (m_Selected == null || Manager == null)
            {
                AudioManager.Instance?.PlayError();
                return;
            }

            bool addedSuccessfully = Manager.TryEnqueue(
                m_Selected,
                first
            );

            if (addedSuccessfully)
            {
                AudioManager.Instance?.PlayAddToQueue();
            }
            else
            {
                AudioManager.Instance?.PlayError();
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
    }
}
