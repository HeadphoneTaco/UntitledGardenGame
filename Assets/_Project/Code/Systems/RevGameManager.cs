using System;
using System.Collections.Generic;
using System.Linq;
using CoreUtils;
using CoreUtils.GameEvents;
using CoreUtils.GameVariables;
using UnityEngine;
using Random = UnityEngine.Random;

namespace RevManager {
    public enum GamePhase {
        Weekday,
        Weekend,
        Finished,
    }

    /// <summary>
    /// Orchestrates the run: 4 weeks of 5 weekday turns, a weekend choice each
    /// Friday, machine retaliation, and the ending evaluation.
    ///
    /// Everything tunable lives either in the Inspector here or in the
    /// ScriptableObject assets (actions, news, weekend options, endings).
    /// The design can change without touching this file.
    ///
    /// UI hooks: bind bars/numbers straight to the GameVariable assets with
    /// CoreUtils SliderBinding / ValueTextBinding, listen to the GameEvent
    /// assets for phase changes, and subscribe to JournalUpdated for the feed.
    /// </summary>
    public class RevGameManager : Singleton<RevGameManager> {
        [Header("Core Bars (GameVariableFloatRange assets)")]
        [SerializeField] private GameVariableFloatRange m_Community;
        [SerializeField] private GameVariableFloatRange m_Machine;

        [Header("Movement")]
        [SerializeField, Tooltip("People in the commune. Grow actions raise this; more people = more action points.")]
        private GameVariableFloat m_People;

        [Header("Clock (GameVariableInt assets so UI can bind to them)")]
        [SerializeField] private GameVariableInt m_Week;
        [SerializeField] private GameVariableInt m_Day;
        [SerializeField] private GameVariableInt m_ActionPointsLeft;

        [Header("Run Length")]
        [SerializeField, Min(1)] private int m_TotalWeeks = 4;
        [SerializeField, Min(1)] private int m_DaysPerWeek = 5;

        [Header("Action Points")]
        [SerializeField, Min(1), Tooltip("Points per day with a minimal commune.")]
        private int m_BaseActionPoints = 2;
        [SerializeField, Min(1), Tooltip("One bonus point per this many people.")]
        private int m_PeoplePerBonusPoint = 10;

        [Header("Machine Pressure")]
        [SerializeField, Tooltip("Community damage from retaliation in week 1.")]
        private float m_BaseRetaliation = 5f;
        [SerializeField, Tooltip("Extra retaliation per completed week. The machine hits harder as it gets desperate.")]
        private float m_RetaliationPerWeek = 5f;
        [SerializeField, Tooltip("Machine bar regained each week. Set above 0 to punish slow play.")]
        private float m_MachineRecoveryPerWeek;

        [Header("News")]
        [SerializeField, Range(0f, 1f)] private float m_NewsChancePerDay = 0.5f;

        [Header("Content Buckets")]
        [SerializeField] private ActionBucket m_Actions;
        [SerializeField] private NewsEventBucket m_News;
        [SerializeField] private WeekendOptionBucket m_WeekendOptions;
        [SerializeField] private EndingBucket m_Endings;

        [Header("Phase Events (optional GameEvent assets for UI / state machine)")]
        [SerializeField] private GameEvent m_OnDayStarted;
        [SerializeField] private GameEvent m_OnWeekendReached;
        [SerializeField] private GameEvent m_OnProtestResolved;
        [SerializeField] private GameEvent m_OnGameEnded;

        private readonly List<JournalEntry> m_Journal = new List<JournalEntry>();
        private readonly HashSet<NewsEventData> m_FiredNews = new HashSet<NewsEventData>();
        private readonly List<ActionData> m_TodaysActions = new List<ActionData>();

        public event Action<JournalEntry> JournalUpdated;
        public event Action<WeekendOptionData, bool> ProtestResolved;
        public event Action<EndingData> GameEnded;

        public GamePhase Phase { get; private set; } = GamePhase.Weekday;
        public IReadOnlyList<JournalEntry> Journal => m_Journal;
        public IReadOnlyList<ActionData> TodaysActions => m_TodaysActions;
        public EndingData Ending { get; private set; }

        public GameVariableFloatRange Community => m_Community;
        public GameVariableFloatRange Machine => m_Machine;
        public GameVariableFloat People => m_People;
        public GameVariableInt Week => m_Week;
        public GameVariableInt Day => m_Day;
        public GameVariableInt ActionPointsLeft => m_ActionPointsLeft;
        public int TotalWeeks => m_TotalWeeks;
        public int DaysPerWeek => m_DaysPerWeek;
        public ActionBucket Actions => m_Actions;
        public WeekendOptionBucket WeekendOptions => m_WeekendOptions;

        private void Start() {
            StartRun();
        }

        public void StartRun() {
            m_Community.ResetValue();
            m_Machine.ResetValue();
            m_People.ResetValue();
            m_Journal.Clear();
            m_FiredNews.Clear();
            Ending = null;

            m_Week.Value = 1;
            m_Day.Value = 1;
            Phase = GamePhase.Weekday;

            // Watch for collapse via Changed, not MinReached: MinReached only fires
            // when a hit lands exactly on the minimum, and most killing blows overshoot.
            // Unsubscribe first so a restart never double-subscribes.
            m_Community.Changed -= OnCommunityChanged;
            m_Community.Changed += OnCommunityChanged;

            BeginDay();
        }

        // ---- Weekday flow ----

        /// <summary>How many points the commune gets today. Growth literally buys time.</summary>
        public int DailyActionPoints => m_BaseActionPoints + Mathf.FloorToInt(m_People.Value / m_PeoplePerBonusPoint);

        public bool CanExecute(ActionData action) {
            return Phase == GamePhase.Weekday
                   && action
                   && action.TimeCost <= m_ActionPointsLeft.Value
                   && action.CanAfford;
        }

        /// <summary>Runs an action immediately and spends its time. A visual timed queue can layer on later.</summary>
        public bool TryExecuteAction(ActionData action) {
            if (!CanExecute(action)) {
                return false;
            }

            m_ActionPointsLeft.Value -= action.TimeCost;
            action.Execute();
            m_TodaysActions.Add(action);
            return true;
        }

        /// <summary>Void wrapper so Button OnClick can call it (UnityEvents hide bool-returning methods).</summary>
        public void ExecuteAction(ActionData action) {
            TryExecuteAction(action);
        }

        public void EndDay() {
            if (Phase != GamePhase.Weekday) {
                return;
            }

            TryFireNews();

            if (m_Day.Value >= m_DaysPerWeek) {
                Phase = GamePhase.Weekend;
                RaiseIfSet(m_OnWeekendReached);
            } else {
                m_Day.Value += 1;
                BeginDay();
            }
        }

        private void BeginDay() {
            m_TodaysActions.Clear();
            m_ActionPointsLeft.Value = DailyActionPoints;
            RaiseIfSet(m_OnDayStarted);
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
                Phase = GamePhase.Weekday;
                BeginDay();
            }
        }

        // ---- News ----

        private void TryFireNews() {
            if (!m_News || Random.value > m_NewsChancePerDay) {
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
            Phase = GamePhase.Finished;
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

        private static void RaiseIfSet(GameEvent gameEvent) {
            if (gameEvent) {
                gameEvent.Raise();
            }
        }
    }
}
