# Revolution Manager: Code Skeleton

Data-driven skeleton for the UFO IV jam game. Design lives in ScriptableObject
assets; code just runs the clock. Changing the design means editing assets in
the Inspector, not editing code.

## The loop

4 weeks. Each week = 5 weekday turns + 1 weekend choice.

- Weekday: queue actions onto a timed belt. The day clock only runs while the
  queue has items (hybrid time: plan frozen, execute live). Costs and effects
  settle when an action FINISHES — cancel is free, but a drained resource can
  make a queued action fall through (journal notes it). Food/Water drain per
  in-game hour while the clock runs. When the queue empties with 0 hours left,
  the day ends itself. One guaranteed news event per day, breaking at a random
  hour mid-day while the belt runs (fallback: fires at day end if the day was
  cut short). After day 5, the weekend unlocks.
- Urgency jumps the line: Add First preempts the running action, which pauses
  with its progress kept and resumes when it's back at the front. News events
  with an UrgentAction get clickable headlines that pull that action into the
  detail card.
- Weekend: choose one option (rest / small action / big mobilization). Success
  requires the Community bar to be above that option's threshold. Tired hungry
  people fail big protests. That rule IS the theme.
- After every weekend the machine retaliates, harder each week.
- Game ends after week 4 (or early, if Community hits zero). The highest
  priority EndingData whose conditions match the two bars is chosen.

## Asset setup checklist (Unity editor, one-time)

1. Variables (right-click > Create > CoreUtils > GameVariable):
   - FloatRange: `Community` (e.g. 0-100, start 50), `Machine` (0-100, start 100)
   - Float: `People` (start ~5), plus FloatRange for `Food`, `Water`, `Health`
   - Int: `Week`, `Day`, `ActionPointsLeft`
   - Suggested folder: `_Project/ScriptableObjects/Variables`
2. Buckets (Create > RevManager > Buckets): one each of Action, News Event,
   Weekend Option, Ending. Set each bucket's Source folder to the matching
   content folder (e.g. `_Project/ScriptableObjects/Actions`). Buckets
   auto-collect every asset in their folder. Drop in a new asset and it's in
   the game.
3. Content (Create > RevManager > ...): ActionData, NewsEventData,
   WeekendOptionData (make 3: Rest / Small / Big), EndingData (make a
   fallback with open conditions + priority 0, and one with IsEarlyCollapse).
4. Scene: empty GameObject with `RevGameManager`, assign the variables,
   buckets, and (optionally) GameEvent assets for phase changes.
5. UI: bind bars with CoreUtils `SliderBinding` (uses FloatRange.Progress) and
   numbers with `ValueTextBinding`. No code needed for display.

## Where things plug in

| Design doc idea            | Asset type          | Field |
|----------------------------|---------------------|-------|
| Resources (food/water/...) | ResourceData + GameVariableFloatRange | |
| Actions (care/grow/fight)  | ActionData          | Type, Costs, Effects |
| Breaking news / journal    | NewsEventData       | Tone (journal color), EarliestWeek, Weight |
| Urgent news response       | NewsEventData       | UrgentAction (clickable headline) |
| Weekend protest choice     | WeekendOptionData   | MinCommunityProgress (fail threshold), RetaliationMultiplier |
| Multiple endings ladder    | EndingData          | MaxMachineProgress, MinCommunityProgress, Priority |
| VCR collage screen         | WeekendOptionData.CollageFrames | |
| Recruit = more capacity    | RevGameManager: BaseActionPoints + PeoplePerBonusPoint | |
| Tier tech tree             | ActionData          | Tier, UnlocksTier, Prerequisites, MinSupporters, Repeatable |

## Code map

- `Data/` : the ScriptableObject definitions (ActionData, NewsEventData,
  WeekendOptionData, EndingData, ResourceData, VariableEffect/VariableCost)
- `Buckets/` : CoreUtils asset buckets that auto-collect content assets
- `Systems/RevGameManager.cs` : the clock, timed queue, resource drain,
  protest resolution, retaliation, news firing, ending selection. UI talks to:
  `TryEnqueue`, `TryCancelQueued`, `CanQueue`, `UnreservedHours`,
  `ActiveProgress`, `QueueVersion`, `EndDay`, `ChooseWeekend`,
  `JournalUpdated`, `GameEnded`.

## UI (UI Toolkit)

The screen is `_Project/UI/RevGameScreen.uxml` + `RevGameScreen.uss`, driven
by `Code/UI/RevGameScreenController.cs` on a UIDocument. Action and weekend
buttons are generated from the buckets at runtime, so new content assets
appear in the UI automatically. Layout and styling changes are text edits to
the uxml/uss files; no scene surgery.

The old uGUI binding components (ActionButtonBinding, WeekendButtonBinding,
NamedValueTextBinding) are superseded by this and safe to delete.

## Inspector setup for the queue/clock (one-time)

1. `RevGameManager` > Day Clock: set **Seconds Per Hour** (1.5 default) and
   assign **Draining Resources** = the `DrainResourceBucket` asset.
2. On the `Food` and `Water` ResourceData assets: set **Drain Per Hour**
   (DrainLabel stays display-only — keep both in sync by hand).
3. The End Day button now only works while the queue is idle; with 0 hours
   left the day ends automatically.

## Not built yet (on purpose)

- EffectsIfIgnored on news (stretch goal from the doc)
- VCR collage payoff screen (weekend resolution currently journal-only)
- Title / pause screens (jam requirement, quick to add at the end)
