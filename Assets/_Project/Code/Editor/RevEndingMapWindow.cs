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
    public partial class RevEndingMapWindow : EditorWindow {
        private const int k_Samples = 96;
        private const float k_Sidebar = 300f;

        private EndingBucket m_Bucket;
        private EndingData[] m_Endings = new EndingData[0];
        private Texture2D m_Map;
        private readonly Dictionary<EndingData, Color> m_Colors = new Dictionary<EndingData, Color>();
        private readonly Dictionary<EndingData, int> m_CellCounts = new Dictionary<EndingData, int>();
        private int m_GapCells;
        private Vector2 m_SidebarScroll;

        // Raw-region highlight: an ending's conditions define a full stencil
        // shape; priority stacks the stencils. This overlays the WHOLE shape
        // for one ending so shadowed area is visible while tuning.
        private EndingData m_Highlight;
        private Texture2D m_Overlay;

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
            wantsMouseMove = true; // Hover feedback on the drag handles.
            Undo.undoRedoPerformed += Refresh;
            Refresh();
        }

        private void OnDisable() {
            Undo.undoRedoPerformed -= Refresh;
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
            if (m_Highlight && !m_Endings.Contains(m_Highlight)) {
                m_Highlight = null;
            }
            BuildOverlayTexture();
            PickAt(m_PickMachine, m_PickCommunity);
            Repaint();
        }

        private void BuildOverlayTexture() {
            if (m_Overlay == null) {
                m_Overlay = new Texture2D(k_Samples, k_Samples, TextureFormat.RGBA32, false) {
                    filterMode = FilterMode.Point,
                    hideFlags = HideFlags.HideAndDontSave,
                };
            }
            Color on = new Color(1f, 1f, 1f, 0.4f);
            Color off = Color.clear;
            for (int y = 0; y < k_Samples; y++) {
                float community = y / (float)(k_Samples - 1);
                for (int x = 0; x < k_Samples; x++) {
                    float machine = x / (float)(k_Samples - 1);
                    bool lit = m_Highlight && m_Highlight.Matches(machine, community);
                    m_Overlay.SetPixel(x, y, lit ? on : off);
                }
            }
            m_Overlay.Apply();
        }

        private void SetHighlight(EndingData ending) {
            m_Highlight = m_Highlight == ending ? null : ending;
            BuildOverlayTexture();
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
            if (Event.current.type == EventType.MouseMove) {
                Repaint(); // Keeps handle hover states tracking the mouse.
            }
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
            if (m_Highlight) {
                GUILayout.Label($"stencil: {m_Highlight.Title}", EditorStyles.toolbarButton);
                if (GUILayout.Button("clear", EditorStyles.toolbarButton, GUILayout.Width(44))) {
                    SetHighlight(m_Highlight);
                }
            }
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
            if (m_Highlight && m_Overlay) {
                GUI.DrawTexture(rect, m_Overlay, ScaleMode.StretchToFill);
            }
            DrawConditionHandles(rect);

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
            if (HandleConditionDrag(rect, e)) {
                return; // A condition edge claimed the mouse.
            }
            if ((e.type != EventType.MouseDown && e.type != EventType.MouseDrag) || !rect.Contains(e.mousePosition)) {
                return;
            }
            PickAt((e.mousePosition.x - rect.x) / rect.width, 1f - (e.mousePosition.y - rect.y) / rect.height);
            e.Use();
            Repaint();
        }

    }
}
