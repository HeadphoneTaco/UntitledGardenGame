using Random = UnityEngine.Random;

namespace RevManager {
    /// <summary>
    /// Day boundaries and the Friday weekend choice — the calendar half of the
    /// run, as opposed to the minute-to-minute belt in RevGameManager.Queue.cs.
    ///
    /// See RevGameManager.cs for the inspector config and state fields.
    /// </summary>
    public partial class RevGameManager {
        /// <summary>
        /// Ends the day. Fires automatically when the queue drains with 0 hours
        /// left; the End Day button can also call it early (queue must be idle).
        /// Each day gets exactly one news event — usually breaking mid-day, but
        /// backstopped here. The world moves whether you do or not.
        /// </summary>
        public void EndDay()
        {
            if (Phase != GamePhase.Weekday || m_Queue.Count > 0)
            {
                return;
            }

            // Every completed day gets Important news.
            FireNews(NewsTone.Important);

            if (m_Day.Value >= m_DaysPerWeek)
            {
                // Wait for the player to close the Important story
                // before opening the weekly Crisis.
                SetPhase(GamePhase.Weekend);
                return;
            }

            m_Day.Value += 1;
            BeginDay();
        }
        
        public void OpenWeeklyCrisis()
        {
            if (Phase != GamePhase.Weekend || HasPendingCrisis)
            {
                return;
            }

            FireNews(NewsTone.Crisis);
        }

        public void BeginNextWeek()
        {
            if (Phase != GamePhase.Weekend)
            {
                return;
            }

            if (m_Week.Value >= m_TotalWeeks)
            {
                // The final Crisis must be resolved before the ending.
                if (!HasPendingCrisis)
                {
                    FinishRun(false);
                }

                return;
            }

            m_Week.Value += 1;
            m_Day.Value = 1;
            SetPhase(GamePhase.Weekday);
            BeginDay();
        }

        private void BeginDay() {
            m_TodaysActions.Clear();
            ClearQueue();
            m_ActionPointsLeft.Value = DailyActionPoints;

            // Roll today's breaking-news hour: somewhere in the middle half of
            // the day, so it lands while plans are in motion.
            m_HoursIntoDay = 0f;
            m_NewsFiredToday = false;
            m_NewsHourToday = Random.Range(0.25f, 0.75f) * DailyActionPoints;

            RaiseIfSet(m_OnDayStarted);
        }

        private void ClearQueue() {
            m_Queue.Clear();
            QueueVersion++;
        }

        // ---- Weekend flow ----

        public bool CanChoose(WeekendOptionData option) {
            return Phase == GamePhase.Weekend && option && option.Costs.CanAffordAll();
        }

        public void ChooseWeekend(WeekendOptionData option) {
            if (Phase != GamePhase.Weekend || !option || !option.Costs.CanAffordAll()) {
                return;
            }

            option.Costs.PayAll();

            // The theme rule: exhausted, hungry people cannot win a big protest.
            bool success = m_Community.Progress >= option.MinCommunityProgress;

            if (success) {
                m_Machine.Value -= option.MachineDamage;
                option.EffectsOnSuccess.ApplyAll();
            } else {
                option.EffectsOnFailure.ApplyAll();
            }

            AddJournalEntry(new JournalEntry(m_Week.Value, m_Day.Value,
                $"{option.DisplayName}: {(success ? "the streets answered." : "it fell apart. People were too worn down.")}",
                success ? NewsTone.Important : NewsTone.Crisis));

            RaiseIfSet(m_OnProtestResolved);
            ProtestResolved?.Invoke(option, success);

            // The machine hits back harder every week, and harder still if provoked.
            float retaliation = (m_BaseRetaliation + m_RetaliationPerWeek * (m_Week.Value - 1)) * option.RetaliationMultiplier;
            m_Community.Value -= retaliation;
            m_Machine.Value += m_MachineRecoveryPerWeek;

            if (Phase == GamePhase.Finished) {
                return; // Retaliation collapsed the community; ending already chosen.
            }

            if (m_Week.Value >= m_TotalWeeks) {
                FinishRun(false);
            } else {
                m_Week.Value += 1;
                m_Day.Value = 1;
                SetPhase(GamePhase.Weekday);
                BeginDay();
            }
        }
    }
}
