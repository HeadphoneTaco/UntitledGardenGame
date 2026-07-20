using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

namespace RevManager {
    /// <summary>
    /// The world talking back: the daily news roll, the journal feed the UI
    /// subscribes to, and how the run's ending gets picked.
    ///
    /// See RevGameManager.cs for the inspector config and state fields.
    /// </summary>
    public partial class RevGameManager {
        /// <summary>Guaranteed one event per day end (per the design doc) — as long as something is eligible.</summary>
        private void FireNews() {
            if (!m_News) {
                return;
            }

            NewsEventData[] eligible = m_News.Items
                .Where(n => n && n.EarliestWeek <= m_Week.Value && !(n.OneTimeOnly && m_FiredNews.Contains(n)))
                .ToArray();

            NewsEventData pick = PickWeighted(eligible);
            if (!pick) {
                return;
            }

            m_FiredNews.Add(pick);
            pick.EffectsOnFire.ApplyAll();
            AddJournalEntry(new JournalEntry(m_Week.Value, m_Day.Value, pick.Headline, pick.Tone, pick));
        }

        private static NewsEventData PickWeighted(NewsEventData[] options) {
            float total = options.Sum(o => o.Weight);
            if (total <= 0f) {
                return null;
            }

            float roll = Random.value * total;
            foreach (NewsEventData option in options) {
                roll -= option.Weight;
                if (roll <= 0f) {
                    return option;
                }
            }
            return options.LastOrDefault();
        }

        private void AddJournalEntry(JournalEntry entry) {
            m_Journal.Add(entry);
            JournalUpdated?.Invoke(entry);
        }

        // ---- Endings ----

        private void OnCommunityChanged(float value) {
            if (Phase != GamePhase.Finished && m_Community.Progress <= 0f) {
                FinishRun(true);
            }
        }

        private void FinishRun(bool earlyCollapse) {
            SetPhase(GamePhase.Finished);
            m_Community.Changed -= OnCommunityChanged;

            Ending = PickEnding(earlyCollapse);
            RaiseIfSet(m_OnGameEnded);
            GameEnded?.Invoke(Ending);
        }

        /// <summary>
        /// Test hook (dev builds; the buttons live on the pause screen):
        /// force both bars to the given 0..1 progress and end the run through
        /// the REAL ending-selection path, so it exercises the same ladder
        /// logic as a played-out run. Community 0 collapses via the genuine
        /// Changed-watcher route.
        /// </summary>
        public void DebugForceEnd(float communityProgress, float machineProgress) {
            if (Phase == GamePhase.Finished) {
                return;
            }
            SetBarProgress(m_Machine, machineProgress);
            SetBarProgress(m_Community, communityProgress); // 0 finishes right here via OnCommunityChanged
            if (Phase != GamePhase.Finished) {
                FinishRun(false);
            }
        }

        /// <summary>
        /// The package hides MaxValue, so scale Value by the current
        /// Value/Progress ratio instead. A bar sitting at 0 gets slammed to
        /// its max first (the setter clamps) so the ratio exists.
        /// </summary>
        private static void SetBarProgress(CoreUtils.GameVariables.GameVariableFloatRange bar, float progress) {
            progress = Mathf.Clamp01(progress);
            if (bar.Progress <= 0.0001f) {
                bar.Value = 999999f;
            }
            if (bar.Progress > 0.0001f) {
                bar.Value = bar.Value / bar.Progress * progress;
            }
        }

        private EndingData PickEnding(bool earlyCollapse) {
            return m_Endings
                ? SelectEnding(m_Endings.Items, m_Machine.Progress, m_Community.Progress, earlyCollapse)
                : null;
        }

        /// <summary>
        /// The single source of truth for ending selection. Static so the
        /// ending coverage map editor window runs the exact same ladder the
        /// game does — if this changes, the map changes with it.
        /// </summary>
        public static EndingData SelectEnding(EndingData[] all, float machineProgress, float communityProgress, bool earlyCollapse) {
            if (all == null) {
                return null;
            }

            if (earlyCollapse) {
                EndingData collapse = all.FirstOrDefault(e => e && e.IsEarlyCollapse);
                if (collapse) {
                    return collapse;
                }
            }

            return all.Where(e => e && !e.IsEarlyCollapse)
                .OrderByDescending(e => e.Priority)
                .FirstOrDefault(e => e.Matches(machineProgress, communityProgress));
        }
    }
}
