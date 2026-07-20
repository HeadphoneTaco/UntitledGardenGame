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
    /// </summary>
    public class RevTechTreeWindow : EditorWindow {
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

        // ---- Layout ----

        private void LayoutNodes() {
            m_NodeRects.Clear();

            // Size every node from its text, then column width = widest node
            // in that tier, so columns only take the room they need.
            var sizes = new Dictionary<ActionData, Vector2>();
            var columnWidth = new float[6];
            for (int tier = 1; tier <= 5; tier++) {
                columnWidth[tier] = kMinNodeWidth;
            }

            var gateWidth = new float[6];
            foreach (ActionData action in m_Actions) {
                GUIContent content = NodeContent(action);
                float width = Mathf.Clamp(m_NodeStyle.CalcSize(content).x + 10, kMinNodeWidth, kMaxNodeWidth);
                float height = Mathf.Max(30f, m_NodeStyle.CalcHeight(content, width) + 8);
                sizes[action] = new Vector2(width, height);
                int column = DisplayColumn(action);
                if (action.UnlocksTier > 0) {
                    gateWidth[column] = Mathf.Max(gateWidth[column], width);
                } else {
                    columnWidth[column] = Mathf.Max(columnWidth[column], width);
                }
            }

            // Tier 0 spark and the Underground root share tier 1's side lane:
            // spark -> underground -> the movement.
            float startWidth = Mathf.Clamp(m_NodeStyle.CalcSize(kStartNode).x + 10, kMinNodeWidth, kMaxNodeWidth);
            float startHeight = Mathf.Max(30f, m_NodeStyle.CalcHeight(kStartNode, startWidth) + 8);
            float sparkWidth = Mathf.Clamp(m_NodeStyle.CalcSize(kSparkNode).x + 10, kMinNodeWidth, kMaxNodeWidth);
            float sparkHeight = Mathf.Max(30f, m_NodeStyle.CalcHeight(kSparkNode, sparkWidth) + 8);
            gateWidth[1] = Mathf.Max(gateWidth[1], sparkWidth + kGateMargin + startWidth);

            // Columns with a gate get a reserved lane on their left, so the
            // gate can sit at any height without overlapping column nodes.
            float x0 = 20;
            for (int tier = 1; tier <= 5; tier++) {
                if (gateWidth[tier] > 0) {
                    x0 += gateWidth[tier] + kGateMargin;
                }
                m_ColumnX[tier] = x0;
                x0 += columnWidth[tier] + kColumnGap;
            }
            m_CanvasWidth = x0;

            // Pass 1: stack the regular nodes.
            var columnY = new float[6];
            for (int tier = 1; tier <= 5; tier++) {
                columnY[tier] = 34;
            }
            foreach (ActionData action in m_Actions) {
                if (action.UnlocksTier > 0) {
                    continue;
                }
                int column = DisplayColumn(action);
                Vector2 size = sizes[action];
                float x = m_ColumnX[column] + (columnWidth[column] - size.x) * 0.5f;
                m_NodeRects[action] = new Rect(x, columnY[column], size.x, size.y);
                columnY[column] += size.y + kRowGap;
            }

            // Both roots center on tier 1's stack, chained in the side lane.
            float stack1 = Mathf.Max(0, columnY[1] - 34 - kRowGap);
            m_StartRect = new Rect(m_ColumnX[1] - startWidth - kGateMargin,
                34 + Mathf.Max(0, (stack1 - startHeight) * 0.5f), startWidth, startHeight);
            m_SparkRect = new Rect(m_StartRect.x - sparkWidth - kGateMargin,
                34 + Mathf.Max(0, (stack1 - sparkHeight) * 0.5f), sparkWidth, sparkHeight);

            // Pass 2: gates hang in their lane, vertically centered on the
            // column they open — the door sits level with the middle of the room.
            for (int column = 1; column <= 5; column++) {
                List<ActionData> gates = m_Actions.Where(a => a.UnlocksTier > 0 && DisplayColumn(a) == column).ToList();
                if (gates.Count == 0) {
                    continue;
                }
                float stackHeight = Mathf.Max(0, columnY[column] - 34 - kRowGap);
                float gatesHeight = gates.Sum(g => sizes[g].y) + (gates.Count - 1) * kRowGap;
                float y = 34 + Mathf.Max(0, (stackHeight - gatesHeight) * 0.5f);
                foreach (ActionData gate in gates) {
                    Vector2 size = sizes[gate];
                    m_NodeRects[gate] = new Rect(m_ColumnX[column] - size.x - kGateMargin, y, size.x, size.y);
                    y += size.y + kRowGap;
                }
            }
        }

        /// <summary>Gates (UnlocksTier > 0) render at the tier they open; everything else at its own tier.</summary>
        private static int DisplayColumn(ActionData action) {
            return Mathf.Clamp(action.UnlocksTier > 0 ? action.UnlocksTier : action.Tier, 1, 5);
        }

        private float CanvasHeight() {
            return m_NodeRects.Count == 0 ? 200 : m_NodeRects.Values.Max(r => r.yMax) + 40;
        }

        // ---- Canvas ----

        private void DrawCanvas(Rect area) {
            m_CanvasScroll = GUI.BeginScrollView(area, m_CanvasScroll, new Rect(0, 0, m_CanvasWidth, CanvasHeight()));

            // Column headers pinned to the layout's column origins, so an
            // empty tier (like 4) can't drift into its neighbor.
            for (int tier = 1; tier <= 4; tier++) {
                GUI.Label(new Rect(m_ColumnX[tier], 8, kMinNodeWidth, 20), $"TIER {tier}", EditorStyles.boldLabel);
            }

            DrawEdges();
            DrawNodes();

            GUI.EndScrollView();
        }

        private void DrawEdges() {
            // Tier 0 spark feeds the Underground root.
            {
                Vector3 from = new Vector3(m_SparkRect.xMax, m_SparkRect.center.y);
                Vector3 to = new Vector3(m_StartRect.xMin, m_StartRect.center.y);
                Handles.DrawBezier(from, to, from + Vector3.right * 20, to + Vector3.left * 20,
                    new Color(1f, 1f, 1f, 0.25f), null, 2f);
            }

            // Faint threads from the Underground root to every tier 1 action,
            // Twine-style: this is where all of it starts.
            foreach (ActionData action in m_Actions) {
                if (action.Tier != 1 || action.UnlocksTier > 0 || !m_NodeRects.ContainsKey(action)) {
                    continue;
                }
                Vector3 from = new Vector3(m_StartRect.xMax, m_StartRect.center.y);
                Vector3 to = new Vector3(m_NodeRects[action].xMin, m_NodeRects[action].center.y);
                Handles.DrawBezier(from, to, from + Vector3.right * 40, to + Vector3.left * 40,
                    new Color(1f, 1f, 1f, 0.12f), null, 1.5f);
            }

            foreach (ActionData action in m_Actions) {
                if (action.Prerequisites == null) {
                    continue;
                }
                foreach (ActionData prerequisite in action.Prerequisites) {
                    if (!prerequisite || !m_NodeRects.ContainsKey(prerequisite)) {
                        continue;
                    }
                    bool involved = action == m_Selected || prerequisite == m_Selected;
                    Vector3 from = new Vector3(m_NodeRects[prerequisite].xMax, m_NodeRects[prerequisite].center.y);
                    Vector3 to = new Vector3(m_NodeRects[action].xMin, m_NodeRects[action].center.y);
                    Color color = involved ? new Color(0.9f, 0.25f, 0.3f) : new Color(1f, 1f, 1f, 0.35f);
                    Handles.DrawBezier(from, to,
                        from + Vector3.right * 60, to + Vector3.left * 60,
                        color, null, involved ? 3.5f : 2f);
                }
            }
        }

        private void DrawNodes() {
            // The root is scenery, not data: greyed out and unclickable.
            bool wasEnabled = GUI.enabled;
            GUI.enabled = false;
            GUI.Button(m_SparkRect, kSparkNode, m_NodeStyle);
            GUI.Button(m_StartRect, kStartNode, m_NodeStyle);
            GUI.enabled = wasEnabled;

            foreach (ActionData action in m_Actions) {
                Rect rect = m_NodeRects[action];
                bool selected = action == m_Selected;

                Color old = GUI.backgroundColor;
                if (selected) {
                    GUI.backgroundColor = new Color(1f, 0.55f, 0.35f);
                } else if (m_Selected && m_Selected.Prerequisites != null && m_Selected.Prerequisites.Contains(action)) {
                    GUI.backgroundColor = new Color(0.95f, 0.35f, 0.4f);
                }

                if (GUI.Button(rect, NodeContent(action), m_NodeStyle)) {
                    OnNodeClicked(action);
                }
                GUI.backgroundColor = old;
            }
        }

        private void OnNodeClicked(ActionData action) {
            if (Event.current.control && m_Selected && m_Selected != action) {
                TogglePrerequisite(m_Selected, action);
                return;
            }
            m_Selected = action;
            EditorGUIUtility.PingObject(action);
            Selection.activeObject = action;
        }

        private void TogglePrerequisite(ActionData target, ActionData prerequisite) {
            Undo.RecordObject(target, "Toggle Prerequisite");
            List<ActionData> list = (target.Prerequisites ?? new ActionData[0]).ToList();
            if (list.Contains(prerequisite)) {
                list.Remove(prerequisite);
            } else {
                list.Add(prerequisite);
            }
            target.Prerequisites = list.ToArray();
            EditorUtility.SetDirty(target);
        }

        // ---- Sidebar ----

        private void DrawSidebar(Rect area) {
            GUILayout.BeginArea(area, EditorStyles.helpBox);
            m_SidebarScroll = EditorGUILayout.BeginScrollView(m_SidebarScroll);

            if (!m_Selected) {
                EditorGUILayout.HelpBox("Select an action node.\n\nCtrl+Click other nodes to link/unlink them as prerequisites.", MessageType.Info);
            } else {
                EditorGUILayout.LabelField(m_Selected.DisplayName, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(m_Selected.name, EditorStyles.miniLabel);
                EditorGUILayout.Space(4);

                EditorGUI.BeginChangeCheck();
                int tier = EditorGUILayout.IntSlider("Tier", m_Selected.Tier, 1, 4);
                int unlocks = EditorGUILayout.IntSlider("Unlocks Tier", m_Selected.UnlocksTier, 0, 4);
                float supporters = EditorGUILayout.FloatField("Min Supporters", m_Selected.MinSupporters);
                bool repeatable = EditorGUILayout.Toggle("Repeatable", m_Selected.Repeatable);
                if (EditorGUI.EndChangeCheck()) {
                    Undo.RecordObject(m_Selected, "Edit Action Unlocks");
                    m_Selected.Tier = tier;
                    m_Selected.UnlocksTier = unlocks;
                    m_Selected.MinSupporters = Mathf.Max(0, supporters);
                    m_Selected.Repeatable = repeatable;
                    EditorUtility.SetDirty(m_Selected);
                }

                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Prerequisites", EditorStyles.boldLabel);
                foreach (ActionData other in m_Actions) {
                    if (other == m_Selected) {
                        continue;
                    }
                    bool has = m_Selected.Prerequisites != null && m_Selected.Prerequisites.Contains(other);
                    bool now = EditorGUILayout.ToggleLeft($"T{other.Tier}  {other.DisplayName}", has);
                    if (now != has) {
                        TogglePrerequisite(m_Selected, other);
                    }
                }
            }

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ---- Sanity ----

        /// <summary>DFS for prerequisite cycles; a cycle means those actions can never unlock.</summary>
        private string FindCycle() {
            var state = new Dictionary<ActionData, int>(); // 0 unvisited, 1 in-stack, 2 done
            foreach (ActionData action in m_Actions) {
                string cycle = Visit(action, state);
                if (cycle != null) {
                    return cycle;
                }
            }
            return null;
        }

        private string Visit(ActionData action, Dictionary<ActionData, int> state) {
            if (!action || state.TryGetValue(action, out int s) && s == 2) {
                return null;
            }
            if (state.TryGetValue(action, out s) && s == 1) {
                return action.name;
            }
            state[action] = 1;
            if (action.Prerequisites != null) {
                foreach (ActionData prerequisite in action.Prerequisites) {
                    string cycle = Visit(prerequisite, state);
                    if (cycle != null) {
                        return cycle;
                    }
                }
            }
            state[action] = 2;
            return null;
        }
    }
}
