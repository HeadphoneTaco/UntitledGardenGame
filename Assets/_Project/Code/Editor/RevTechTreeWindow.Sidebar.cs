using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RevManager.EditorTools {
    /// <summary>
    /// The right-hand inspector panel for the selected node, plus the graph
    /// sanity check that warns about prerequisite cycles.
    ///
    /// See RevTechTreeWindow.cs for the controls and shared state.
    /// </summary>
    public partial class RevTechTreeWindow {
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
