using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RevManager.EditorTools {
    /// <summary>
    /// Where every node lands: text-driven node sizes, per-tier column widths,
    /// the reserved gate lanes, and the two scenery root nodes.
    ///
    /// See RevTechTreeWindow.cs for the controls and shared state.
    /// </summary>
    public partial class RevTechTreeWindow {
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
    }
}
