using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RevManager {
    /// <summary>
    /// Tools > RevManager > Ending Coverage Map.
    ///
    /// Paints the whole end-state space — machine progress across, community
    /// progress up — colored by which ending the real selection ladder picks
    /// (RevGameManager.SelectEnding, the same code the game runs). Uncovered
    /// states glow magenta so gaps are impossible to miss; the bottom row is
    /// the community-collapse route.
    ///
    /// Click anywhere on the map to read the ending that fires there (title,
    /// win/lose, body text) — the tone-versus-bar-strength vibe check. The
    /// legend lists every ending with its conditions and how much of the
    /// space it actually claims; 0.0% means it can never be reached.
    /// </summary>
    public class RevEndingMapWindow : EditorWindow {
        private const int k_Samples = 96;
        private const float k_Sidebar = 300f;

        private EndingBucket m_Bucket;
        private EndingData[] m_Endings = new EndingData[0];
        private Texture2D m_Map;
        private readonly Dictionary<EndingData, Color> m_Colors = new Dictionary<EndingData, Color>();
        private readonly Dictionary<EndingData, int> m_CellCounts = new Dictionary<EndingData, int>();
        private int m_GapCells;
        private Vector2 m_SidebarScroll;

        // Inspected point (map click), progress space 0..1.
        private float m_PickMachine = 0.5f;
        private float m_PickCommunity = 0.5f;
        private EndingData m_Picked;

        private static readonly Color s_GapColor = new Color(1f, 0f, 0.85f);

        [MenuItem("Tools/RevManager/Ending Coverage Map")]
        public static void Open() {
            GetWindow<RevEndingMapWindow>("Ending Coverage");
        }

        private void OnEnable() {
            wantsMouseMove = false;
            Refresh();
        }

        // ---- Data ----

        private void Refresh() {
            if (!m_Bucket) {
                string guid = AssetDatabase.FindAssets("t:EndingBucket").FirstOrDefault();
                if (guid != null) {
                    m_Bucket = AssetDatabase.LoadAssetAtPath<EndingBucket>(AssetDatabase.GUIDToAssetPath(guid));
                }
            }
            m_Endings = m_Bucket ? m_Bucket.Items.Where(e => e).ToArray() : new EndingData[0];

            m_Colors.Clear();
            for (int i = 0; i < m_Endings.Length; i++) {
                // Golden-ratio hue walk: consecutive endings land far apart on
                // the wheel, so neighbors on the map stay tellable-apart.
                m_Colors[m_Endings[i]] = Color.HSVToRGB(i * 0.618034f % 1f, 0.55f, 0.85f);
            }

            BuildMapTexture();
            PickAt(m_PickMachine, m_PickCommunity);
            Repaint();
        }

        private void BuildMapTexture() {
            if (m_Map == null) {
                m_Map = new Texture2D(k_Samples, k_Samples, TextureFormat.RGBA32, false) {
                    filterMode = FilterMode.Point,
                    hideFlags = HideFlags.HideAndDontSave,
                };
            }

            m_CellCounts.Clear();
            m_GapCells = 0;

            for (int y = 0; y < k_Samples; y++) {
                float community = y / (float)(k_Samples - 1);
                for (int x = 0; x < k_Samples; x++) {
                    float machine = x / (float)(k_Samples - 1);
                    EndingData ending = Select(machine, community);
                    if (ending) {
                        m_CellCounts[ending] = m_CellCounts.GetValueOrDefault(ending) + 1;
                        m_Map.SetPixel(x, y, m_Colors.GetValueOrDefault(ending, Color.gray));
                    } else {
                        m_GapCells++;
                        m_Map.SetPixel(x, y, s_GapColor);
                    }
                }
            }
            m_Map.Apply();
        }

        /// <summary>Community 0 collapses mid-run in game, so the bottom edge routes through the collapse ending.</summary>
        private EndingData Select(float machine, float community) {
            return RevGameManager.SelectEnding(m_Endings, machine, community, community <= 0.0001f);
        }

        private void PickAt(float machine, float community) {
            m_PickMachine = Mathf.Clamp01(machine);
            m_PickCommunity = Mathf.Clamp01(community);
            m_Picked = Select(m_PickMachine, m_PickCommunity);
        }

        // ---- GUI ----

        private void OnGUI() {
            DrawToolbar();

            Rect content = new Rect(8, 28, position.width - k_Sidebar - 24, position.height - 60);
            float side = Mathf.Min(content.width, content.height);
            Rect mapRect = new Rect(content.x + 30, content.y + 8, side - 38, side - 38);

            DrawMap(mapRect);
            DrawAxes(mapRect);
            HandleMapMouse(mapRect);

            Rect sidebar = new Rect(position.width - k_Sidebar - 8, 28, k_Sidebar, position.height - 36);
            GUILayout.BeginArea(sidebar);
            m_SidebarScroll = GUILayout.BeginScrollView(m_SidebarScroll);
            DrawInspector();
            EditorGUILayout.Space(10);
            DrawLegend();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawToolbar() {
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60))) {
                Refresh();
            }
            m_Bucket = (EndingBucket)EditorGUILayout.ObjectField(m_Bucket, typeof(EndingBucket), false, GUILayout.Width(160));
            GUILayout.FlexibleSpace();
            if (m_GapCells > 0) {
                float pct = m_GapCells * 100f / (k_Samples * k_Samples);
                GUILayout.Label($"⚠ {pct:0.0}% of end states match NO ending", ErrorStyle());
            } else if (m_Endings.Length > 0) {
                GUILayout.Label("Full coverage — every end state has an ending");
            }
            GUILayout.EndHorizontal();
        }

        private void DrawMap(Rect rect) {
            if (m_Map == null) {
                return;
            }
            EditorGUI.DrawRect(new Rect(rect.x - 1, rect.y - 1, rect.width + 2, rect.height + 2), Color.black);
            GUI.DrawTexture(rect, m_Map, ScaleMode.StretchToFill);

            // Crosshair on the inspected point.
            float px = rect.x + m_PickMachine * rect.width;
            float py = rect.y + (1f - m_PickCommunity) * rect.height;
            EditorGUI.DrawRect(new Rect(px - 6, py - 0.5f, 12, 1), Color.white);
            EditorGUI.DrawRect(new Rect(px - 0.5f, py - 6, 1, 12), Color.white);
        }

        private static void DrawAxes(Rect rect) {
            GUIStyle small = EditorStyles.miniLabel;
            GUI.Label(new Rect(rect.x, rect.yMax + 2, rect.width, 16), "MACHINE 0% ————————————→ 100%", small);
            GUIStyle rot = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter };
            // Vertical axis: cheap and readable beats clever — three labels.
            GUI.Label(new Rect(rect.x - 30, rect.y - 4, 28, 16), "C:100", rot);
            GUI.Label(new Rect(rect.x - 30, rect.center.y - 8, 28, 16), "C:50", rot);
            GUI.Label(new Rect(rect.x - 30, rect.yMax - 12, 28, 16), "C:0", rot);
        }

        private void HandleMapMouse(Rect rect) {
            Event e = Event.current;
            if ((e.type != EventType.MouseDown && e.type != EventType.MouseDrag) || !rect.Contains(e.mousePosition)) {
                return;
            }
            PickAt((e.mousePosition.x - rect.x) / rect.width, 1f - (e.mousePosition.y - rect.y) / rect.height);
            e.Use();
            Repaint();
        }

        private void DrawInspector() {
            EditorGUILayout.LabelField("Inspected end state", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"MACHINE {m_PickMachine * 100f:0}%   COMMUNITY {m_PickCommunity * 100f:0}%");

            if (!m_Picked) {
                EditorGUILayout.HelpBox("No ending matches here. A run ending on these bars shows the missing-ending fallback text. Widen a condition or add a fallback ending with open conditions at priority 0.", MessageType.Error);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            DrawSwatch(m_Colors.GetValueOrDefault(m_Picked, Color.gray));
            EditorGUILayout.LabelField($"{m_Picked.Title}  [{(m_Picked.IsVictory ? "WIN art" : "LOSE art")}]", EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(Conditions(m_Picked), EditorStyles.miniLabel);
            EditorGUILayout.LabelField(m_Picked.Body, WrappedStyle());
            if (GUILayout.Button("Select asset", GUILayout.Width(90))) {
                Selection.activeObject = m_Picked;
                EditorGUIUtility.PingObject(m_Picked);
            }
        }

        private void DrawLegend() {
            EditorGUILayout.LabelField("All endings (ladder order: highest priority first)", EditorStyles.boldLabel);
            int total = k_Samples * k_Samples;

            foreach (EndingData ending in m_Endings.OrderByDescending(e => e.Priority)) {
                EditorGUILayout.BeginHorizontal();
                DrawSwatch(m_Colors.GetValueOrDefault(ending, Color.gray));

                float pct = m_CellCounts.GetValueOrDefault(ending) * 100f / total;
                string tag = ending.IsEarlyCollapse ? "COLLAPSE" : ending.IsVictory ? "WIN" : "LOSE";
                string reach = !ending.IsEarlyCollapse && pct <= 0f ? "  ⚠ never reached" : $"  {pct:0.0}%";
                EditorGUILayout.LabelField($"{ending.Title}  [{tag}]{reach}", GUILayout.MinWidth(120));
                if (GUILayout.Button("→", GUILayout.Width(24))) {
                    Selection.activeObject = ending;
                    EditorGUIUtility.PingObject(ending);
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.LabelField("     " + Conditions(ending), EditorStyles.miniLabel);
            }

            if (!m_Endings.Any(e => e.IsEarlyCollapse)) {
                EditorGUILayout.HelpBox("No early-collapse ending: a mid-run community wipe falls through to the normal ladder.", MessageType.Warning);
            }
            if (m_GapCells > 0) {
                EditorGUILayout.BeginHorizontal();
                DrawSwatch(s_GapColor);
                EditorGUILayout.LabelField("UNCOVERED — no ending matches", EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
            }
        }

        private static string Conditions(EndingData e) {
            if (e.IsEarlyCollapse) {
                return "fires when community collapses to 0 mid-run";
            }
            string lead = "";
            if (e.MinCommunityLead > -1f || e.MaxCommunityLead < 1f) {
                lead = e.MaxCommunityLead < 1f
                    ? $"  lead {e.MinCommunityLead * 100f:+0;-0}..{e.MaxCommunityLead * 100f:+0;-0}"
                    : $"  lead ≥ {e.MinCommunityLead * 100f:+0;-0}";
            }
            return $"machine ≤ {e.MaxMachineProgress * 100f:0}%  community ≥ {e.MinCommunityProgress * 100f:0}%{lead}  (priority {e.Priority})";
        }

        private static void DrawSwatch(Color color) {
            Rect r = GUILayoutUtility.GetRect(16, 16, GUILayout.Width(16));
            EditorGUI.DrawRect(new Rect(r.x, r.y + 1, 14, 14), color);
        }

        private static GUIStyle WrappedStyle() {
            return new GUIStyle(EditorStyles.label) { wordWrap = true, richText = false };
        }

        private static GUIStyle ErrorStyle() {
            return new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = new Color(1f, 0.4f, 0.4f) } };
        }
    }
}
