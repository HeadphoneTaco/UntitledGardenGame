using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace RevManager {
    /// <summary>
    /// The polled display refresh: every 100ms, push game state into the top
    /// bar, resource rows (pending chips + gain/loss value flash), action
    /// rows, detail card, queue panel, and the crisis TV timer.
    ///
    /// See RevGameScreenController.cs for the layout map and field list.
    /// </summary>
    public partial class RevGameScreenController {
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

            // Net queued delta per resource: costs subtract, gains add, so
            // each stock can show what the current plan will do to it.
            m_PendingDeltas.Clear();
            foreach (QueuedAction entry in Manager.Queue) {
                if (entry.Action.Costs != null) {
                    foreach (VariableCost cost in entry.Action.Costs) {
                        if (cost.Variable) {
                            m_PendingDeltas[cost.Variable] = m_PendingDeltas.GetValueOrDefault(cost.Variable) - cost.Amount;
                        }
                    }
                }
                if (entry.Action.Effects != null) {
                    foreach (VariableEffect effect in entry.Action.Effects) {
                        if (effect.Variable) {
                            m_PendingDeltas[effect.Variable] = m_PendingDeltas.GetValueOrDefault(effect.Variable) + effect.Delta;
                        }
                    }
                }
            }

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

                // Flash the number green/red for a moment when it moves
                // (design ask: numbers shine on gain/loss).
                if (float.IsNaN(rowBinding.LastValue)) {
                    rowBinding.LastValue = current;
                } else if (!Mathf.Approximately(current, rowBinding.LastValue)) {
                    rowBinding.FlashGain = current > rowBinding.LastValue;
                    rowBinding.FlashUntil = Time.unscaledTime + 1f;
                    rowBinding.LastValue = current;
                }
                bool flashing = Time.unscaledTime < rowBinding.FlashUntil;
                rowBinding.Value.EnableInClassList("drain-row__value--gain", flashing && rowBinding.FlashGain);
                rowBinding.Value.EnableInClassList("drain-row__value--loss", flashing && !rowBinding.FlashGain);

                // Pending chip: net effect of everything on the belt.
                float pendingDelta = m_PendingDeltas.GetValueOrDefault(rowBinding.Resource.Variable);
                bool hasPending = !Mathf.Approximately(pendingDelta, 0f);
                rowBinding.Pending.text = hasPending ? $"{pendingDelta:+0;-0}" : "";
                rowBinding.Pending.EnableInClassList("drain-row__pending--gain", pendingDelta > 0f);
                rowBinding.Pending.EnableInClassList("drain-row__pending--loss", pendingDelta < 0f);
            }

            // Center panel. Tier-locked and finished one-shots are hidden (the
            // tree reveals itself); prereq-locked rows show greyed so the
            // player knows something is there to unlock.
            // Rows stay CLICKABLE while blocked, so the player can select one
            // and read the reason in the detail card; the class just greys it.
            foreach ((Button button, ActionData action) in m_ActionButtons) {
                bool visible = Manager.IsVisible(action);
                button.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                // One rule everywhere: a row greys whenever it can't join the
                // queue right now — prereqs, supplies, or man-hours alike.
                bool blocked = Manager.Phase != GamePhase.Weekday
                    || !Manager.PrerequisitesMet(action) || !action.CanAfford
                    || action.TimeCost > Manager.UnreservedHours;
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
            // The non-resource requirements flush red by the same rule as
            // supply costs, so the card never says one thing and shows another.
            m_SelectedTimeLine?.EnableInClassList("detail-line--missing",
                m_Selected && m_Selected.TimeCost > Manager.UnreservedHours);
            m_SelectedSupportersLine?.EnableInClassList("detail-line--missing",
                m_Selected && Manager.People.Value < m_Selected.MinSupporters);

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
