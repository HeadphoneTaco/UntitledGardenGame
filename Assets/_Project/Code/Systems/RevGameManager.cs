using System;
using System.Collections.Generic;
using CoreUtils;
using CoreUtils.GameEvents;
using CoreUtils.GameVariables;
using UnityEngine;

namespace RevManager {
    public enum GamePhase {
        Weekday,
        Weekend,
        Finished,
    }

    /// <summary>
    /// One item on the belt. Each entry keeps its own remaining hours so a
    /// preempted action pauses with its progress intact and resumes when it
    /// reaches the front again.
    /// </summary>
    public class QueuedAction {
        public ActionData Action;
        public float HoursRemaining;

        /// <summary>0..1 done. Frozen while the entry isn't at the front.</summary>
        public float Progress => Action && Action.TimeCost > 0
            ? 1f - Mathf.Clamp01(HoursRemaining / Action.TimeCost)
            : 0f;
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
    ///
    /// Split across partial files, one per concern:
    ///   RevGameManager.cs        - inspector config, state, run setup, phase changes
    ///   RevGameManager.Queue.cs  - the action belt: gating, enqueue/cancel, the tick
    ///   RevGameManager.Days.cs   - day boundaries and the weekend choice
    ///   RevGameManager.News.cs   - news rolls, the journal, and ending selection
    /// </summary>
    public partial class RevGameManager : Singleton<RevGameManager> {
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

        // Crisis currently waiting for an Attend or Ignore response.
        private NewsEventData m_PendingCrisis;
        private float m_CrisisHoursRemaining;
        private readonly List<JournalEntry> m_Journal = new List<JournalEntry>();
        private readonly HashSet<NewsEventData> m_FiredNews = new HashSet<NewsEventData>();
        private readonly List<ActionData> m_TodaysActions = new List<ActionData>();
        private readonly HashSet<ActionData> m_CompletedThisRun = new HashSet<ActionData>();
        private readonly List<QueuedAction> m_Queue = new List<QueuedAction>();

        // One guaranteed news event per day, BREAKING at a random hour while
        // the belt runs (so urgent headlines can interrupt live plans). If the
        // day ends before that hour is reached, it fires at day end instead.
        private float m_HoursIntoDay;
        private float m_NewsHourToday;
        private bool m_NewsFiredToday;

        public event Action<JournalEntry> JournalUpdated;
        public event Action<WeekendOptionData, bool> ProtestResolved;
        public event Action<EndingData> GameEnded;
        public event Action<ActionData> ActionCompleted;
        public event Action<NewsEventData> CrisisStarted;
        public event Action<NewsEventData, bool> CrisisResolved;

        public GamePhase Phase { get; private set; } = GamePhase.Weekday;
        
        public NewsEventData PendingCrisis => m_PendingCrisis;
        public bool HasPendingCrisis => m_PendingCrisis != null;
        
        public IReadOnlyList<JournalEntry> Journal => m_Journal;
        public IReadOnlyList<ActionData> TodaysActions => m_TodaysActions;
        public IReadOnlyList<QueuedAction> Queue => m_Queue;
        public EndingData Ending { get; private set; }
        public float CrisisHoursRemaining => m_CrisisHoursRemaining;

        /// <summary>Bumped on every queue mutation so the UI can rebuild only when needed.</summary>
        public int QueueVersion { get; private set; }

        /// <summary>Tech-tree tier the commune has reached. Starts at 1; tier actions raise it.</summary>
        public int CurrentTier { get; private set; } = 1;

        /// <summary>0..1 progress of the item at the front of the queue.</summary>
        public float ActiveProgress => m_Queue.Count > 0 ? m_Queue[0].Progress : 0f;

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
            
            m_PendingCrisis = null;
            m_CrisisHoursRemaining = 0f;
            m_Community.ResetValue();
            m_Machine.ResetValue();
            m_People.ResetValue();
            m_Journal.Clear();
            m_FiredNews.Clear();
            m_CompletedThisRun.Clear();
            CurrentTier = 1;
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
