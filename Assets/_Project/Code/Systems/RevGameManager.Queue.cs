using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RevManager {
    /// <summary>
    /// The action belt: what the player is allowed to queue, how items get on
    /// and off the queue, and the per-frame tick that works the front item.
    ///
    /// Hybrid time: the day clock is frozen while the queue is empty (plan in
    /// peace), and runs at m_SecondsPerHour while it has items — like a
    /// factory belt that only moves when there's something on it. Costs and
    /// effects settle when an action FINISHES, so canceling never needs a
    /// refund, but a resource can drain out from under a queued action (it
    /// gets skipped with a journal note).
    ///
    /// See RevGameManager.cs for the inspector config and state fields.
    /// </summary>
    public partial class RevGameManager {
        /// <summary>How many points the commune gets today. Growth literally buys time.</summary>
        public int DailyActionPoints => m_BaseActionPoints + Mathf.FloorToInt(m_People.Value / m_PeoplePerBonusPoint);

        /// <summary>
        /// Hours already promised to the queue. Full TimeCost per entry even if
        /// partly done — AP is paid in full at completion regardless of pauses.
        /// </summary>
        public int QueuedHours => m_Queue.Sum(e => e.Action.TimeCost);

        /// <summary>Hours neither spent nor queued — the number the player can still plan with.</summary>
        public int UnreservedHours => m_ActionPointsLeft.Value - QueuedHours;

        /// <summary>Completed at least once this run (persists across days, resets on restart).</summary>
        public bool IsCompleted(ActionData action) {
            return m_CompletedThisRun.Contains(action);
        }

        /// <summary>
        /// Tier-locked actions are HIDDEN (design call: the tree reveals itself).
        /// Prereq-locked actions within a reached tier stay VISIBLE but greyed,
        /// so the player can see what to work toward.
        /// </summary>
        public bool IsVisible(ActionData action) {
            return action
                   && action.Tier <= CurrentTier
                   && (action.Repeatable || !IsCompleted(action));
        }

        public bool PrerequisitesMet(ActionData action) {
            if (!action || action.Prerequisites == null) {
                return true;
            }
            foreach (ActionData prerequisite in action.Prerequisites) {
                if (prerequisite && !IsCompleted(prerequisite)) {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Prerequisites still standing between the player and this action, for UI messaging.</summary>
        public IEnumerable<ActionData> MissingPrerequisites(ActionData action) {
            if (!action || action.Prerequisites == null) {
                yield break;
            }
            foreach (ActionData prerequisite in action.Prerequisites) {
                if (prerequisite && !IsCompleted(prerequisite)) {
                    yield return prerequisite;
                }
            }
        }

        /// <summary>
        /// Gates at queue time: visibility, the supporter requirement, and time.
        /// Resource costs are still checked on completion.
        /// </summary>
        public bool CanQueue(ActionData action) {
            return Phase == GamePhase.Weekday
                   && IsVisible(action)
                   && PrerequisitesMet(action)
                   && m_People.Value >= action.MinSupporters
                   && action.TimeCost <= UnreservedHours
                   // A one-shot can't be queued twice.
                   && (action.Repeatable || m_Queue.All(e => e.Action != action));
        }

        /// <summary>
        /// Adds an action to the queue. "First" PREEMPTS: it goes straight to
        /// the front and the running action pauses where it stands, resuming
        /// with its progress intact when it's back on top. Urgency jumps the line.
        /// </summary>
        public bool TryEnqueue(ActionData action, bool first) {
            if (!CanQueue(action)) {
                return false;
            }

            var entry = new QueuedAction { Action = action, HoursRemaining = action.TimeCost };
            if (first) {
                m_Queue.Insert(0, entry);
            } else {
                m_Queue.Add(entry);
            }

            QueueVersion++;
            return true;
        }

        /// <summary>Void wrapper so Button OnClick can call it (UnityEvents hide bool-returning methods).</summary>
        public void ExecuteAction(ActionData action) {
            TryEnqueue(action, false);
        }

        /// <summary>
        /// Removes a queued item. Nothing was paid yet, so there's nothing to
        /// refund — canceling the active item just throws away its progress.
        /// The expected action guards against a stale index from the UI.
        /// </summary>
        public bool TryCancelQueued(int index, ActionData expected) {
            if (index < 0 || index >= m_Queue.Count || m_Queue[index].Action != expected) {
                return false;
            }

            m_Queue.RemoveAt(index);
            QueueVersion++;
            return true;
        }

        private void Update()
        {
            if (Phase != GamePhase.Weekday)
            {
                return;
            }

            if (m_Queue.Count == 0) {
                // Auto day-end: out of time and nothing left on the belt.
                if (m_ActionPointsLeft.Value <= 0) {
                    EndDay();
                }
                return;
            }

            float hours = Time.deltaTime / m_SecondsPerHour;
            DrainResources(hours);
            
            if (HasPendingCrisis)
            {
                m_CrisisHoursRemaining -= hours;

                if (m_CrisisHoursRemaining <= 0f)
                {
                    IgnorePendingCrisis();
                }
            }

            // Drain (or a skipped action's fallout) can collapse the community
            // mid-tick, which changes Phase via the Changed callback.
            if (Phase != GamePhase.Weekday) return;
            
            // Breaking news hits mid-shift, not after everyone's gone home.
            m_HoursIntoDay += hours;
            if (!m_NewsFiredToday && m_HoursIntoDay >= m_NewsHourToday) {
                m_NewsFiredToday = true;
                FireNews();
                if (Phase != GamePhase.Weekday) return;
            }

            m_Queue[0].HoursRemaining -= hours;
            while (m_Queue.Count > 0 && m_Queue[0].HoursRemaining <= 0f && Phase == GamePhase.Weekday) {
                float overshoot = m_Queue[0].HoursRemaining; // <= 0
                CompleteFront();
                if (m_Queue.Count > 0) {
                    // Carry the overshoot so back-to-back items keep smooth timing.
                    m_Queue[0].HoursRemaining += overshoot;
                }
            }
        }

        /// <summary>Food/Water tick down only while the belt is moving. Time spent is life spent.</summary>
        private void DrainResources(float hours) {
            if (!m_DrainingResources) {
                return;
            }

            foreach (ResourceData resource in m_DrainingResources.Items) {
                if (resource && resource.Variable && resource.DrainPerHour > 0f) {
                    resource.Variable.Value -= resource.DrainPerHour * hours;
                }
            }
        }

        private void CompleteFront() {
            ActionData action = m_Queue[0].Action;
            m_Queue.RemoveAt(0);
            QueueVersion++;

            // Pay-on-completion: all checks happen here, at the last moment.
            // (The one-shot check covers a duplicate that slipped into the queue.)
            if (action.TimeCost > m_ActionPointsLeft.Value || !action.CanAfford
                || !action.Repeatable && IsCompleted(action)) {
                AddJournalEntry(new JournalEntry(m_Week.Value, m_Day.Value,
                    $"{action.DisplayName} fell through. Not enough left to see it done.", NewsTone.Crisis));
                return;
            }

            m_ActionPointsLeft.Value -= action.TimeCost;
            action.Execute();
            m_TodaysActions.Add(action);
            m_CompletedThisRun.Add(action);
            ActionCompleted?.Invoke(action);

            // Tier actions push the whole tree open. New actions appear in the
            // list on the next UI poll; the journal announces the shift.
            if (action.UnlocksTier > CurrentTier) {
                CurrentTier = action.UnlocksTier;
                AddJournalEntry(new JournalEntry(m_Week.Value, m_Day.Value,
                    $"{action.DisplayName} changes everything. The movement can attempt more.", NewsTone.Important));
            }
        }
    }
}
