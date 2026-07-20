using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RevManager.EditorTools {
    /// <summary>
    /// Tools > RevManager > Tech Tree Editor
    ///
    /// Visual editor for the action unlock graph. Columns = tiers, curves =
    /// prerequisites (drawn from the required action into the one that needs it).
    ///
    /// Controls:
    ///   Click a node          - select it (also pings the normal Inspector)
    ///   Ctrl+Click a node     - toggle it as a PREREQUISITE of the selected node
    ///   Sidebar               - tier/unlock/supporters/repeatable + prereq checkboxes
    ///
    /// Everything writes straight to the ActionData assets with Undo support.
    ///
    /// Split across partial files, one per concern:
    ///   RevTechTreeWindow.cs         - state, asset loading, OnGUI frame, header rows
    ///   RevTechTreeWindow.Layout.cs  - node sizing and column/gate placement
    ///   RevTechTreeWindow.Canvas.cs  - drawing nodes + prerequisite curves, node clicks
    ///   RevTechTreeWindow.Sidebar.cs - the inspector sidebar and the cycle check
    /// </summary>
    public partial class RevTechTreeWindow : EditorWindow {
        private const float kMinNodeWidth = 150f;
        private const float kMaxNodeWidth = 280f;
        private const float kColumnGap = 90f;
        private const float kRowGap = 12f;
        private const float kSidebarWidth = 270f;
        private const float kHeaderHeight = 42f; // toolbar + legend rows

        private GUIStyle m_NodeStyle;
        private float m_CanvasWidth;
        private readonly float[] m_ColumnX = new float[6];
        private const float kGateMargin = 24f; // gap between a gate node and the column it opens

        // The design chart's root: not an ActionData, just where the story starts.
        private static readonly GUIContent kStartNode = new GUIContent("Tier 1 - Underground\nstart of the run");
        private Rect m_StartRect;

        // And the spark before even that.
        private static readonly GUIContent kSparkNode = new GUIContent("Tier 0\nthe people have had enough");
        private Rect m_SparkRect;

        private List<ActionData> m_Actions = new List<ActionData>();
        private readonly Dictionary<ActionData, Rect> m_NodeRects = new Dictionary<ActionData, Rect>();
        private ActionData m_Selected;
        private Vector2 m_CanvasScroll;
        private Vector2 m_SidebarScroll;

        [MenuItem("Tools/RevManager/Tech Tree Editor")]
        public static void Open() {
            var window = GetWindow<RevTechTreeWindow>("Rev Tech Tree");
            window.minSize = new Vector2(900, 450);
            window.Reload();
        }

        private void OnEnable() {
            Reload();
        }

        private void Reload() {
            m_Actions = AssetDatabase.FindAssets($"t:{nameof(ActionData)}")
                .Select(guid => AssetDatabase.LoadAssetAtPath<ActionData>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(a => a)
                .OrderBy(a => a.Tier)
                .ThenBy(a => a.UnlocksTier > 0 ? 1 : 0) // tier-advance actions sink to the bottom of their column
                .ThenBy(a => a.name)
                .ToList();
            if (!m_Actions.Contains(m_Selected)) {
                m_Selected = null;
            }
            Repaint();
        }

        private void OnGUI() {
            if (m_NodeStyle == null) {
                m_NodeStyle = new GUIStyle(EditorStyles.miniButton) {
                    wordWrap = true,
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 12,
                    padding = new RectOffset(8, 8, 5, 5),
                    // miniButton ships a fixedHeight; while it's set, CalcHeight
                    // ignores content and every node comes out one line tall.
                    fixedHeight = 0,
                };
            }

            DrawToolbar();
            DrawLegend();

            var canvasArea = new Rect(0, kHeaderHeight, position.width - kSidebarWidth, position.height - kHeaderHeight);
            var sidebarArea = new Rect(position.width - kSidebarWidth, kHeaderHeight, kSidebarWidth, position.height - kHeaderHeight);

            LayoutNodes();
            DrawCanvas(canvasArea);
            DrawSidebar(sidebarArea);
        }

        /// <summary>Name line + a plain-words badge line, shared by layout and draw.</summary>
        private static GUIContent NodeContent(ActionData action) {
            string label = string.IsNullOrEmpty(action.DisplayName) ? action.name : action.DisplayName;

            var badges = new List<string>();
            if (action.UnlocksTier > 0) {
                badges.Add($"unlocks tier {action.UnlocksTier}");
            }
            if (!action.Repeatable) {
                badges.Add("one-shot");
            }
            if (action.MinSupporters > 0) {
                badges.Add($"needs {action.MinSupporters:0} support");
            }

            return new GUIContent(badges.Count > 0 ? $"{label}\n{string.Join("  |  ", badges)}" : label);
        }

        private void DrawToolbar() {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60))) {
                Reload();
            }
            GUILayout.Label($"{m_Actions.Count} actions", EditorStyles.miniLabel);
            string cycle = FindCycle();
            if (cycle != null) {
                var style = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = Color.red } };
                GUILayout.Label($"CYCLE: {cycle} - these can never unlock!", style);
            }
            GUILayout.FlexibleSpace();
            GUILayout.Label("Click = select   Ctrl+Click = toggle prerequisite of selected", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawLegend() {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("unlocks tier N = completing this opens that tier's actions      one-shot = disappears after completing once      needs N support = Support stat must reach N before it can be queued", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }
    }
}
