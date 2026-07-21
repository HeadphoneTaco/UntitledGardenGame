using System.Collections.Generic;
using CoreUtils.GameVariables;
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
                m_QueueRowBindings.Clear();
                m_ActiveQueueFill = null;

                IReadOnlyList<QueuedAction> queue = Manager.Queue;
                for (int i = 0; i < queue.Count; i++) {
                    QueuedAction entry = queue[i];
                    ActionData action = entry.Action;
                    int index = i;

                    // Row click inspects (detail card); the ✕ cancels. Explicit
                    // beats hover — runtime panels don't render tooltips anyway.
                    var row = new VisualElement();
                    row.AddToClassList("queue-row");
                    row.RegisterCallback<ClickEvent>(_ => SelectAction(action));

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

                    var cancel = new Button(() => OnQueueRowClicked(index, action)) { text = "✕" };
                    cancel.AddToClassList("queue-row__cancel");
                    RegisterButtonAudio(cancel);
                    // Swallow the click so canceling doesn't also select.
                    cancel.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());

                    row.Add(icon);
                    row.Add(col);
                    row.Add(cancel);
                    m_QueueList.Add(row);
                    m_QueueRowBindings.Add((row, action));

                    if (i == 0) {
                        m_ActiveQueueFill = fill;
                    }
                }
            }

            // Live drain on the working item: full bar -> empty as its hours run out.
            if (m_ActiveQueueFill != null) {
                m_ActiveQueueFill.style.width = Length.Percent((1f - Manager.ActiveProgress) * 100f);
            }

            RefreshQueueRisk();
        }

        /// <summary>
        /// Costs settle at completion, so a queued item can be living on
        /// credit: fine when an earlier item will produce the goods, fatal
        /// when it won't. Walk the belt in order, banking each item's costs
        /// and gains on paper AND projecting Food/Water drain to each item's
        /// completion hour, then tint any row whose bill won't clear — a
        /// forecast at enqueue time, not a thermometer at failure time.
        /// Slightly pessimistic on purpose (drain is projected even past a
        /// resource's floor): a warning that fires early beats one that
        /// fires as the belt delivers the failure.
        /// </summary>
        private void RefreshQueueRisk() {
            IReadOnlyList<QueuedAction> queue = Manager.Queue;
            if (queue.Count != m_QueueRowBindings.Count) {
                return; // Rebuild pending this poll; next poll is in 100ms.
            }

            m_ProjectedPool.Clear();
            float projectedHours = Manager.ActionPointsLeft.Value;
            float hoursFromNow = 0f; // When each item settles, from this instant.

            for (int i = 0; i < queue.Count; i++) {
                ActionData action = m_QueueRowBindings[i].action;
                hoursFromNow += queue[i].HoursRemaining; // Front item keeps its partial progress.

                bool funded = action.TimeCost <= projectedHours
                    && ProjectedShortfall(action, hoursFromNow) == null;
                m_QueueRowBindings[i].row.EnableInClassList("queue-row--at-risk", !funded);
                if (!funded) {
                    continue; // A failed item settles nothing (matches fall-through).
                }

                projectedHours -= action.TimeCost;
                if (action.Costs != null) {
                    foreach (VariableCost cost in action.Costs) {
                        if (cost.Variable) {
                            m_ProjectedPool[cost.Variable] = ProjectedSettlements(cost.Variable) - cost.Amount;
                        }
                    }
                }
                if (action.Effects != null) {
                    foreach (VariableEffect effect in action.Effects) {
                        if (effect.Variable) {
                            m_ProjectedPool[effect.Variable] = ProjectedSettlements(effect.Variable) + effect.Delta;
                        }
                    }
                }
            }
        }

        /// <summary>Current value plus banked queue settlements — drain excluded (it's applied per-completion-time in the afford check).</summary>
        private float ProjectedSettlements(GameVariableFloat variable) {
            return m_ProjectedPool.TryGetValue(variable, out float value) ? value : variable.Value;
        }

        /// <summary>Name of the first resource this action can't cover at its settlement time, or null if all clear.</summary>
        private string ProjectedShortfall(ActionData action, float hoursFromNow) {
            if (action.Costs == null) {
                return null;
            }
            foreach (VariableCost cost in action.Costs) {
                if (!cost.Variable) {
                    continue;
                }
                float available = ProjectedSettlements(cost.Variable) - DrainRate(cost.Variable) * hoursFromNow;
                if (available < cost.Amount) {
                    return cost.Variable.name.ToUpperInvariant();
                }
            }
            return null;
        }

        /// <summary>Drain per hour for the variable, from the resource list (0 for non-draining).</summary>
        private float DrainRate(GameVariableFloat variable) {
            foreach (ResourceRow row in m_ResourceRows) {
                if (row.Resource && row.Resource.Variable == variable) {
                    return row.Resource.DrainPerHour;
                }
            }
            return 0f;
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
