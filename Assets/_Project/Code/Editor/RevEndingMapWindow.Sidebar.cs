using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RevManager {
    /// <summary>
    /// Sidebar half of the ending coverage map: the inspected end state
    /// readout and the legend of all endings with coverage shares.
    ///
    /// See RevEndingMapWindow.cs for the map itself and the data model.
    /// </summary>
    public partial class RevEndingMapWindow {
        private void DrawInspector() {
            if (m_Highlight && !m_Highlight.IsEarlyCollapse) {
                EditorGUILayout.HelpBox($"Editing \"{m_Highlight.Title}\": drag the white edges (machine cap / community floor) or yellow diagonals (lead band) directly on the map. Ctrl+Z undoes.", MessageType.Info);
            }
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
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Select asset", GUILayout.Width(90))) {
                Selection.activeObject = m_Picked;
                EditorGUIUtility.PingObject(m_Picked);
            }
            if (GUILayout.Button(m_Highlight == m_Picked ? "Hide stencil" : "Show stencil", GUILayout.Width(90))) {
                SetHighlight(m_Picked);
            }
            EditorGUILayout.EndHorizontal();
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
                bool lit = m_Highlight == ending;
                if (GUILayout.Toggle(lit, "◈", EditorStyles.miniButton, GUILayout.Width(24)) != lit) {
                    SetHighlight(ending);
                }
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
