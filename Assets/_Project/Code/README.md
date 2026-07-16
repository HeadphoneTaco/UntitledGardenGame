# Revolution Manager: Code Skeleton

Data-driven skeleton for the UFO IV jam game. Design lives in ScriptableObject
assets; code just runs the clock. Changing the design means editing assets in
the Inspector, not editing code.

## The loop

4 weeks. Each week = 5 weekday turns + 1 weekend choice.

- Weekday: spend action points on actions (instant resolve). End Day may fire
  a news event into the journal. After day 5, the weekend unlocks.
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
| Weekend protest choice     | WeekendOptionData   | MinCommunityProgress (fail threshold), RetaliationMultiplier |
| Multiple endings ladder    | EndingData          | MaxMachineProgress, MinCommunityProgress, Priority |
| VCR collage screen         | WeekendOptionData.CollageFrames | |
| Recruit = more capacity    | RevGameManager: BaseActionPoints + PeoplePerBonusPoint | |

## Code map

- `Data/` : the ScriptableObject definitions (ActionData, NewsEventData,
  WeekendOptionData, EndingData, ResourceData, VariableEffect/VariableCost)
- `Buckets/` : CoreUtils asset buckets that auto-collect content assets
- `Systems/RevGameManager.cs` : the clock, protest resolution, retaliation,
  news firing, ending selection. UI talks to: `TryExecuteAction`, `EndDay`,
  `ChooseWeekend`, `JournalUpdated`, `GameEnded`.

## Not built yet (on purpose)

- All UI (Terraformental-style layout comes next)
- Timed/visual action queue (actions resolve instantly for now; a queue is
  presentation on top of the same calls)
- EffectsIfIgnored on news (stretch goal from the doc)
- Title / pause / end screens (jam requirement, quick to add at the end)
