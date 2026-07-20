using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RevManager.EditorTools {
    /// <summary>
    /// The scrolling graph itself: column headers, prerequisite curves, node
    /// buttons, and what a click does to them.
    ///
    /// See RevTechTreeWindow.cs for the controls and shared state.
    /// </summary>
    public partial class RevTechTreeWindow {
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
    }
}
