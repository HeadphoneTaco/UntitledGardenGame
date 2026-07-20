using System.Linq;
using CoreUtils.GameVariables;
using UnityEditor;
using UnityEngine;

namespace RevManager.EditorTools {
    /// <summary>
    /// Tools > RevManager > Generate Starter Content
    ///
    /// Creates a playable starter set: resource variables (if missing),
    /// actions, placeholder news events, and the ending ladder. Never
    /// overwrites an existing asset, so it is safe to re-run and safe to
    /// hand-edit everything it makes. Numbers are first-guess balance;
    /// tune freely in the Inspector.
    ///
    /// News bodies are PLACEHOLDERS for tone/shape. Dulce owns the real copy.
    /// </summary>
    public static class RevContentGenerator {
        private const string Root = "Assets/_Project/ScriptableObjects";

        private static GameVariableFloat s_Community;
        private static GameVariableFloat s_Machine;
        private static GameVariableFloat s_People;

        [MenuItem("Tools/RevManager/Generate Starter Content")]
        public static void Generate() {
            EnsureFolder($"{Root}/Variables");
            EnsureFolder($"{Root}/Actions");
            EnsureFolder($"{Root}/NewsEvents");
            EnsureFolder($"{Root}/Endings");

            s_Community = FindVar("Community");
            s_Machine = FindVar("Machine");
            s_People = FindVar("People");
            if (!s_Community || !s_Machine || !s_People) {
                Debug.LogWarning("RevContentGenerator: Community, Machine, or People variable not found. Effects pointing at them will be left empty.");
            }

            GameVariableFloat food = EnsureRangeVar("Food", 0, 40, 15);
            GameVariableFloat water = EnsureRangeVar("Water", 0, 40, 15);
            GameVariableFloat health = EnsureRangeVar("Health", 0, 40, 20);

            // ---- Care ----
            Action("Tend the Farms", "The mountains feed those who work them.", ActionType.Community, 1,
                Costs(), Effects((food, 8)));
            Action("Haul Clean Water", "The river is far and the state pipes pass the commune by.", ActionType.Community, 1,
                Costs(), Effects((water, 8)));
            Action("Community Kitchen", "Nobody organizes on an empty stomach.", ActionType.Community, 1,
                Costs((food, 5), (water, 2)), Effects((s_Community, 7)));
            Action("Run the Clinic", "Care that the system never provided.", ActionType.Community, 1,
                Costs((water, 3)), Effects((health, 8), (s_Community, 3)));
            Action("Build Together", "Housing raised by many hands.", ActionType.Community, 2,
                Costs((food, 4)), Effects((s_Community, 12)));

            // ---- Grow ----
            Action("Door to Door", "Listen first. Then ask.", ActionType.Organize, 1,
                Costs((food, 2)), Effects((s_People, 2)));
            Action("Print the Message", "Posters go up faster than they tear them down.", ActionType.Organize, 1,
                Costs(), Effects((s_People, 1), (s_Community, 2)));
            Action("Art Festival", "Joy is recruitment.", ActionType.Organize, 2,
                Costs((food, 5), (water, 3)), Effects((s_People, 4), (s_Community, 6)));
            Action("Live Stream", "Visibility brings friends, and attention.", ActionType.Organize, 1,
                Costs(), Effects((s_People, 3), (s_Community, -2)));

            // ---- Fight ----
            Action("Tear Down Propaganda", "Their story, removed from the walls.", ActionType.Resist, 1,
                Costs(), Effects((s_Machine, -3)));
            Action("Work Slowdown", "Everything done exactly by the book, and no faster.", ActionType.Resist, 1,
                Costs((s_Community, 3)), Effects((s_Machine, -5)));
            Action("Sabotage the Depot", "A machine is only as strong as its weakest coupling.", ActionType.Resist, 2,
                Costs((s_Community, 4)), Effects((s_Machine, -9)));
            Action("Draft Policies", "Write the world you want. Make them read it.", ActionType.Resist, 2,
                Costs(), Effects((s_Machine, -2), (s_Community, 5)));

            // ---- News (placeholder copy; real material is Dulce's) ----
            News("Rations seized at the northern checkpoint", NewsTone.Crisis, 1, 1f,
                Effects((food, -5)));
            News("A well runs dry", NewsTone.Important, 1, 1f,
                Effects((water, -5)));
            News("Neighbors ask about the kitchen", NewsTone.Flavor, 1, 1.5f,
                Effects((s_People, 1)));
            News("Editorial calls the movement 'a nuisance'", NewsTone.Flavor, 1, 1.5f,
                Effects());
            News("Police sweep the market district", NewsTone.Important, 2, 1f,
                Effects((s_Community, -4)));
            News("A nurse defects to the clinic", NewsTone.Flavor, 2, 1f,
                Effects((s_People, 1), (health, 3)));
            News("Curfew declared", NewsTone.Crisis, 3, 1f,
                Effects((s_Community, -6)));
            News("State TV admits 'irregularities'", NewsTone.Important, 3, 1f,
                Effects((s_Machine, -2)));

            // ---- Endings (doc ladder; higher priority checked first) ----
            Ending("The Movement Is Crushed", "The commune could not hold. People drift away hungry and afraid. Somewhere, someone keeps a poster folded in a drawer.",
                1f, 0f, 0, true);
            Ending("A Symbolic Protest", "The march happens. The photos are taken. Nothing changes, this time.",
                1f, 0f, 0, false);
            Ending("An Occupation", "A territory held, a space carved out where the rules are yours. It is small. It is real.",
                0.6f, 0.2f, 20, false);
            Ending("A Municipality Liberated", "The town runs itself now. The machine pretends it never wanted it.",
                0.4f, 0.25f, 30, false);
            Ending("Mass Uprising", "The streets fill and a bill is signed with shaking hands. The next fight is already forming.",
                0.2f, 0.3f, 40, false);
            Ending("Revolution", "The machine grinds to a halt, out of fuel, out of fear to spend. There is always more battle to fight. Today, you choose the battlefield.",
                0.02f, 0.35f, 50, false);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("RevManager starter content generated. Existing assets were left untouched. Check the buckets picked everything up, then tune numbers in the Inspector.");
        }

        // ---- Helpers ----

        private static void EnsureFolder(string path) {
            if (AssetDatabase.IsValidFolder(path)) {
                return;
            }
            string parent = path.Substring(0, path.LastIndexOf('/'));
            string leaf = path.Substring(path.LastIndexOf('/') + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        private static GameVariableFloat FindVar(string name) {
            return AssetDatabase.FindAssets($"t:{nameof(GameVariableFloat)} {name}")
                .Select(guid => AssetDatabase.LoadAssetAtPath<GameVariableFloat>(AssetDatabase.GUIDToAssetPath(guid)))
                .FirstOrDefault(v => v && v.name.Equals(name, System.StringComparison.OrdinalIgnoreCase));
        }

        private static GameVariableFloat EnsureRangeVar(string name, float min, float max, float initial) {
            GameVariableFloat existing = FindVar(name);
            if (existing) {
                return existing;
            }

            var variable = ScriptableObject.CreateInstance<GameVariableFloatRange>();
            var so = new SerializedObject(variable);
            so.FindProperty("m_MinValue").floatValue = min;
            so.FindProperty("m_MaxValue").floatValue = max;
            so.FindProperty("m_InitialValue").floatValue = initial;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(variable, $"{Root}/Variables/{name}.asset");
            return variable;
        }

        private static VariableEffect[] Effects(params (GameVariableFloat variable, float delta)[] items) {
            return items.Where(i => i.variable)
                .Select(i => new VariableEffect { Variable = i.variable, Delta = i.delta })
                .ToArray();
        }

        private static VariableCost[] Costs(params (GameVariableFloat variable, float amount)[] items) {
            return items.Where(i => i.variable)
                .Select(i => new VariableCost { Variable = i.variable, Amount = i.amount })
                .ToArray();
        }

        private static void Action(string name, string description, ActionType type, int timeCost, VariableCost[] costs, VariableEffect[] effects) {
            string path = $"{Root}/Actions/{name}.asset";
            if (AssetDatabase.LoadAssetAtPath<ActionData>(path)) {
                return;
            }
            var action = ScriptableObject.CreateInstance<ActionData>();
            action.DisplayName = name;
            action.Description = description;
            action.Type = type;
            action.TimeCost = timeCost;
            action.Costs = costs;
            action.Effects = effects;
            AssetDatabase.CreateAsset(action, path);
        }

        private static void News(string headline, NewsTone tone, int earliestWeek, float weight, VariableEffect[] effects) {
            string path = $"{Root}/NewsEvents/{headline.Replace("'", "")}.asset";
            if (AssetDatabase.LoadAssetAtPath<NewsEventData>(path)) {
                return;
            }
            var news = ScriptableObject.CreateInstance<NewsEventData>();
            news.Headline = headline;
            news.Body = "(Placeholder copy for tone and shape. Real material: Dulce.)";
            news.Tone = tone;
            news.EarliestWeek = earliestWeek;
            news.Weight = weight;
            news.EffectsOnFire = effects;
            AssetDatabase.CreateAsset(news, path);
        }

        private static void Ending(string title, string body, float maxMachine, float minCommunity, int priority, bool earlyCollapse) {
            string path = $"{Root}/Endings/{title}.asset";
            if (AssetDatabase.LoadAssetAtPath<EndingData>(path)) {
                return;
            }
            var ending = ScriptableObject.CreateInstance<EndingData>();
            ending.Title = title;
            ending.Body = body;
            ending.MaxMachineProgress = maxMachine;
            ending.MinCommunityProgress = minCommunity;
            ending.Priority = priority;
            ending.IsEarlyCollapse = earlyCollapse;
            AssetDatabase.CreateAsset(ending, path);
        }
    }
}
