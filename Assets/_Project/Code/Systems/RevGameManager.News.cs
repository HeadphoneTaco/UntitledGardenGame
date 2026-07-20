using System.Linq;
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

        private EndingData PickEnding(bool earlyCollapse) {
            if (!m_Endings) {
                return null;
            }

            EndingData[] all = m_Endings.Items;

            if (earlyCollapse) {
                EndingData collapse = all.FirstOrDefault(e => e && e.IsEarlyCollapse);
                if (collapse) {
                    return collapse;
                }
            }

            return all.Where(e => e && !e.IsEarlyCollapse)
                .OrderByDescending(e => e.Priority)
                .FirstOrDefault(e => e.Matches(m_Machine.Progress, m_Community.Progress));
        }
    }
}
