using UnityEditor;
using UnityEngine;

namespace RevManager {
    /// <summary>
    /// Drag-editing for the highlighted ending's condition edges, right on
    /// the map. With a stencil lit (◈), its boundary lines become handles:
    /// white vertical = machine cap, white horizontal = community floor,
    /// yellow diagonals = the lead band. Grab and pull; the map recomputes
    /// live, writes to the asset, and supports undo.
    ///
    /// See RevEndingMapWindow.cs for the map and data model.
    /// </summary>
    public partial class RevEndingMapWindow {
        private enum ConditionHandle {
            None,
            MaxMachine,
            MinCommunity,
            MinLead,
            MaxLead,
        }

        private const float k_GrabPx = 8f;
        private ConditionHandle m_DragHandle;

        private static Vector2 ToGui(Rect r, float machine, float community) {
            return new Vector2(r.x + machine * r.width, r.y + (1f - community) * r.height);
        }

        private void DrawConditionHandles(Rect r) {
            if (!m_Highlight || m_Highlight.IsEarlyCollapse) {
                return;
            }
            EndingData e = m_Highlight;

            // Hover feedback: the line under the mouse (or being dragged)
            // fattens up so it reads as grabbable.
            ConditionHandle hover = m_DragHandle;
            Vector2 mouse = Event.current.mousePosition;
            if (hover == ConditionHandle.None && r.Contains(mouse)) {
                hover = PickHandle(
                    (mouse.x - r.x) / r.width,
                    1f - (mouse.y - r.y) / r.height,
                    k_GrabPx / r.width);
            }

            Handles.color = Color.white;
            // machine ≤ X: vertical line, allowed region to its left.
            Handles.DrawAAPolyLine(Thickness(hover, ConditionHandle.MaxMachine),
                ToGui(r, e.MaxMachineProgress, 0f), ToGui(r, e.MaxMachineProgress, 1f));
            // community ≥ Y: horizontal line, allowed region above.
            Handles.DrawAAPolyLine(Thickness(hover, ConditionHandle.MinCommunity),
                ToGui(r, 0f, e.MinCommunityProgress), ToGui(r, 1f, e.MinCommunityProgress));

            Handles.color = new Color(1f, 0.85f, 0.25f);
            DrawLeadLine(r, e.MinCommunityLead, Thickness(hover, ConditionHandle.MinLead));
            DrawLeadLine(r, e.MaxCommunityLead, Thickness(hover, ConditionHandle.MaxLead));
        }

        private static float Thickness(ConditionHandle hover, ConditionHandle line) {
            return hover == line ? 7f : 3f;
        }

        /// <summary>A lead line is community = machine + lead, clipped to the unit square.</summary>
        private static void DrawLeadLine(Rect r, float lead, float thickness) {
            float m0 = Mathf.Max(0f, -lead);
            float m1 = Mathf.Min(1f, 1f - lead);
            if (m1 <= m0) {
                return; // Off the map (lead at ±1 default): nothing to grab.
            }
            Handles.DrawAAPolyLine(thickness, ToGui(r, m0, m0 + lead), ToGui(r, m1, m1 + lead));
        }

        /// <summary>Returns true when the event was consumed by a condition edge.</summary>
        private bool HandleConditionDrag(Rect r, Event e) {
            if (!m_Highlight || m_Highlight.IsEarlyCollapse) {
                m_DragHandle = ConditionHandle.None;
                return false;
            }

            float machine = (e.mousePosition.x - r.x) / r.width;
            float community = 1f - (e.mousePosition.y - r.y) / r.height;

            switch (e.type) {
                case EventType.MouseDown when r.Contains(e.mousePosition):
                    m_DragHandle = PickHandle(machine, community, k_GrabPx / r.width);
                    if (m_DragHandle == ConditionHandle.None) {
                        return false;
                    }
                    Undo.RecordObject(m_Highlight, "Edit Ending Conditions");
                    e.Use();
                    return true;

                case EventType.MouseDrag when m_DragHandle != ConditionHandle.None:
                    ApplyDrag(machine, community);
                    EditorUtility.SetDirty(m_Highlight);
                    BuildMapTexture();
                    BuildOverlayTexture();
                    PickAt(m_PickMachine, m_PickCommunity);
                    e.Use();
                    Repaint();
                    return true;

                case EventType.MouseUp when m_DragHandle != ConditionHandle.None:
                    m_DragHandle = ConditionHandle.None;
                    e.Use();
                    return true;
            }
            return false;
        }

        private ConditionHandle PickHandle(float machine, float community, float threshold) {
            EndingData e = m_Highlight;
            float lead = community - machine;
            // Diagonals get first grab: they're the fiddlier target.
            if (LeadVisible(e.MinCommunityLead) && Mathf.Abs(lead - e.MinCommunityLead) < threshold * 1.4f) {
                return ConditionHandle.MinLead;
            }
            if (LeadVisible(e.MaxCommunityLead) && Mathf.Abs(lead - e.MaxCommunityLead) < threshold * 1.4f) {
                return ConditionHandle.MaxLead;
            }
            if (Mathf.Abs(machine - e.MaxMachineProgress) < threshold) {
                return ConditionHandle.MaxMachine;
            }
            if (Mathf.Abs(community - e.MinCommunityProgress) < threshold) {
                return ConditionHandle.MinCommunity;
            }
            return ConditionHandle.None;
        }

        private static bool LeadVisible(float lead) {
            return lead > -1f + 0.001f && lead < 1f - 0.001f;
        }

        private void ApplyDrag(float machine, float community) {
            EndingData e = m_Highlight;
            float lead = Mathf.Clamp(community - machine, -1f, 1f);
            switch (m_DragHandle) {
                case ConditionHandle.MaxMachine:
                    e.MaxMachineProgress = Snap(Mathf.Clamp01(machine));
                    break;
                case ConditionHandle.MinCommunity:
                    e.MinCommunityProgress = Snap(Mathf.Clamp01(community));
                    break;
                case ConditionHandle.MinLead:
                    e.MinCommunityLead = Mathf.Min(Snap(lead), e.MaxCommunityLead);
                    break;
                case ConditionHandle.MaxLead:
                    e.MaxCommunityLead = Mathf.Max(Snap(lead), e.MinCommunityLead);
                    break;
            }
        }

        /// <summary>Snap to whole percents so hand-dragged values stay tidy.</summary>
        private static float Snap(float value) {
            return Mathf.Round(value * 100f) / 100f;
        }
    }
}
