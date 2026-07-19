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

        [Header("Day Clock (hybrid: only runs while the queue is working)")]
        [SerializeField, Min(0.1f), Tooltip("Real seconds per in-game hour while the queue is processing. Lower = faster days.")]
        private float m_SecondsPerHour = 1.5f;
        [SerializeField, Tooltip("DrainResourceBucket (Food/Water). These lose DrainPerHour while the clock runs.")]
        private ResourceBucket m_DrainingResources;

        [Header("Game Flow (CoreUtils StateMachine)")]
        [SerializeField, Tooltip("StateMachine with child GameObjects named Weekday, Weekend, and Ending. Mirrors the phase so scene objects can react via State/StateEvents. Optional but recommended.")]
        private StateMachine m_GameFlow;

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
        private readonly List<ActionData> m_Queue = new List<ActionData>();
        private float m_ActiveHoursRemaining;

        public event Action<JournalEntry> JournalUpdated;
        public event Action<WeekendOptionData, bool> ProtestResolved;
        public event Action<EndingData> GameEnded;

        public GamePhase Phase { get; private set; } = GamePhase.Weekday;
        public IReadOnlyList<JournalEntry> Journal => m_Journal;
        public IReadOnlyList<ActionData> TodaysActions => m_TodaysActions;
        public IReadOnlyList<ActionData> Queue => m_Queue;
        public EndingData Ending { get; private set; }

        /// <summary>Bumped on every queue mutation so the UI can rebuild only when needed.</summary>
        public int QueueVersion { get; private set; }

        /// <summary>0..1 progress of the item at the front of the queue.</summary>
        public float ActiveProgress => m_Queue.Count > 0 && m_Queue[0].TimeCost > 0
            ? 1f - Mathf.Clamp01(m_ActiveHoursRemaining / m_Queue[0].TimeCost)
            : 0f;

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
            ClearQueue();
            Ending = null;

            m_Week.Value = 1;
            m_Day.Value = 1;
            SetPhase(GamePhase.Weekday);

            // Watch for collapse via Changed, not MinReached: MinReached only fires
            // when a hit lands exactly on the minimum, and most killing blows overshoot.
            // Unsubscribe first so a restart never double-subscribes.
            m_Community.Changed -= OnCommunityChanged;
            m_Community.Changed += OnCommunityChanged;

            BeginDay();
        }

        // ---- Weekday flow ----
        //
        // Hybrid time: the day clock is frozen while the queue is empty (plan in
        // peace), and runs at m_SecondsPerHour while it has items — like a
        // factory belt that only moves when there's something on it. Costs and
        // effects settle when an action FINISHES, so canceling never needs a
        // refund, but a resource can drain out from under a queued action (it
        // gets skipped with a journal note).

        /// <summary>How many points the commune gets today. Growth literally buys time.</summary>
        public int DailyActionPoints => m_BaseActionPoints + Mathf.FloorToInt(m_People.Value / m_PeoplePerBonusPoint);

        /// <summary>Hours already promised to the queue. Adds are gated on what's left after this.</summary>
        public int QueuedHours => m_Queue.Sum(a => a.TimeCost);

        /// <summary>Hours neither spent nor queued — the number the player can still plan with.</summary>
        public int UnreservedHours => m_ActionPointsLeft.Value - QueuedHours;

        /// <summary>Time is the only gate at queue time; resource costs are checked on completion.</summary>
        public bool CanQueue(ActionData action) {
            return Phase == GamePhase.Weekday && action && action.TimeCost <= UnreservedHours;
        }

        /// <summary>
        /// Adds an action to the queue. "First" slots in right behind the item
        /// already in progress (never preempts it mid-run).
        /// </summary>
        public bool TryEnqueue(ActionData action, bool first) {
            if (!CanQueue(action)) {
                return false;
            }

            if (first && m_Queue.Count > 0) {
                m_Queue.Insert(1, action);
            } else if (first) {
                m_Queue.Insert(0, action);
            } else {
                m_Queue.Add(action);
            }

            if (m_Queue.Count == 1) {
                m_ActiveHoursRemaining = m_Queue[0].TimeCost;
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
            if (index < 0 || index >= m_Queue.Count || m_Queue[index] != expected) {
                return false;
            }

            m_Queue.RemoveAt(index);
            if (index == 0 && m_Queue.Count > 0) {
                m_ActiveHoursRemaining = m_Queue[0].TimeCost;
            }
            QueueVersion++;
            return true;
        }

        private void Update() {
            if (Phase != GamePhase.Weekday) {
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

            // Drain (or a skipped action's fallout) can collapse the community
            // mid-tick, which changes Phase via the Changed callback.
            if (Phase != GamePhase.Weekday) {
                return;
            }

            m_ActiveHoursRemaining -= hours;
            while (m_ActiveHoursRemaining <= 0f && m_Queue.Count > 0 && Phase == GamePhase.Weekday) {
                CompleteFront();
                if (m_Queue.Count > 0) {
                    // Carry the overshoot so back-to-back items keep smooth timing.
                    m_ActiveHoursRemaining += m_Queue[0].TimeCost;
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
            ActionData action = m_Queue[0];
            m_Queue.RemoveAt(0);
            QueueVersion++;

            // Pay-on-completion: both checks happen here, at the last moment.
            if (action.TimeCost > m_ActionPointsLeft.Value || !action.CanAfford) {
                AddJournalEntry(new JournalEntry(m_Week.Value, m_Day.Value,
                    $"{action.DisplayName} fell through. Not enough left to see it done.", NewsTone.Crisis));
                return;
            }

            m_ActionPointsLeft.Value -= action.TimeCost;
            action.Execute();
            m_TodaysActions.Add(action);
        }

        /// <summary>
        /// Ends the day. Fires automatically when the queue drains with 0 hours
        /// left; the End Day button can also call it early (queue must be idle).
        /// Every day ends with a news event — the world moves whether you do or not.
        /// </summary>
        public void EndDay() {
            if (Phase != GamePhase.Weekday || m_Queue.Count > 0) {
                return;
            }

            FireNews();

            if (m_Day.Value >= m_DaysPerWeek) {
                SetPhase(GamePhase.Weekend);
                RaiseIfSet(m_OnWeekendReached);
            } else {
                m_Day.Value += 1;
                BeginDay();
            }
        }

        private void BeginDay() {
            m_TodaysActions.Clear();
            ClearQueue();
            m_ActionPointsLeft.Value = DailyActionPoints;
            RaiseIfSet(m_OnDayStarted);
        }

        private void ClearQueue() {
            m_Queue.Clear();
            m_ActiveHoursRemaining = 0f;
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

        // ---- News ----

        /// <summary>Guaranteed one event per day end (Noah's design) — as long as something is eligible.</summary>
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

        /// <summary>
        /// Phase changes route through here so the CoreUtils StateMachine stays in
        /// sync. The machine activates the matching child GameObject, letting any
        /// scene object react to phases via State/StateEvents with no code.
        /// </summary>
        private void SetPhase(GamePhase phase) {
            Phase = phase;
            if (m_GameFlow) {
                switch (phase) {
                    case GamePhase.Weekday:
                        m_GameFlow.ChangeState("Weekday");
                        break;
                    case GamePhase.Weekend:
                        m_GameFlow.ChangeState("Weekend");
                        break;
                    case GamePhase.Finished:
                        m_GameFlow.ChangeState("Ending");
                        break;
                }
            }
        }

        private static void RaiseIfSet(GameEvent gameEvent) {
            if (gameEvent) {
                gameEvent.Raise();
            }
        }
    }
}
