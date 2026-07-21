using System.Collections.Generic;
using System.Linq;
using CoreUtils.GameVariables;
using UnityEngine.UIElements;

namespace RevManager {
    /// <summary>
    /// Center panel: which action is selected, what the detail card shows, and
    /// why it can't be queued right now.
    ///
    /// See RevGameScreenController.cs for the layout map and field list.
    /// </summary>
    public partial class RevGameScreenController {
        private void SelectAction(ActionData action) {
            m_Selected = action;

            foreach ((Button button, ActionData a) in m_ActionButtons) {
                button.EnableInClassList("action-row--selected", a == action);
            }

            m_DetailIcon.style.backgroundImage = action && action.Icon ? new StyleBackground(action.Icon) : new StyleBackground();
            m_DetailName.text = action ? action.DisplayName : "Select an action";
            m_DetailDesc.text = action ? action.Description : "";

            m_DetailCosts.Clear();
            m_DetailGains.Clear();
            if (!action) {
                m_SelectedTimeLine = null;
                m_SelectedSupportersLine = null;
                return;
            }

            m_SelectedCostLines.Clear();
            m_SelectedTimeLine = AddDetailLine(m_DetailCosts, $"{action.TimeCost} MAN-HOURS");
            m_SelectedSupportersLine = null;
            if (action.MinSupporters > 0f) {
                // A requirement, not a cost: nothing is spent, but it gives
                // tier actions a visible goal to push toward.
                m_SelectedSupportersLine = AddDetailLine(m_DetailCosts, $"NEEDS {action.MinSupporters:0} SUPPORTERS");
            }
            if (action.Costs != null) {
                foreach (VariableCost cost in action.Costs) {
                    if (cost.Variable) {
                        Label line = AddResourceDetailLine(
                            m_DetailCosts,
                            $"x{cost.Amount:0}",
                            cost.Variable
                        );
                        m_SelectedCostLines.Add((line, cost));
                    }
                }
            }

            if (action.Effects != null) {
                foreach (VariableEffect effect in action.Effects) {
                    if (effect.Variable) {
                        AddResourceDetailLine(
                            m_DetailGains,
                            $"{effect.Delta:+0;-0}",
                            effect.Variable
                        );
                    }
                }
            }
        }

        private static Label AddDetailLine(VisualElement container, string text) {
            var line = new Label(text);
            line.AddToClassList("detail-line");
            container.Add(line);
            return line;
        }
        
        private Label AddResourceDetailLine(
            VisualElement container,
            string amountText,
            GameVariableFloat variable)
        {
            ResourceData resource = m_Resources
                ? m_Resources.Items.FirstOrDefault(
                    item => item && item.Variable == variable)
                : null;

            var row = new VisualElement();
            row.AddToClassList("detail-resource-line");

            if (resource && resource.Icon)
            {
                var icon = new VisualElement();
                icon.AddToClassList("detail-resource-line__icon");
                icon.style.backgroundImage = new StyleBackground(resource.Icon);
                row.Add(icon);
            }

            string resourceName = resource
                ? resource.DisplayName
                : variable.Name;

            var label = new Label(
                $"{amountText} {resourceName.ToUpperInvariant()}"
            );

            label.AddToClassList("detail-line");

            row.Add(label);
            container.Add(row);

            return label;
        }

        /// <summary>
        /// Why the selected action can't be queued right now. Null = it can.
        /// Ordered by what the player should fix first.
        /// </summary>
        /// <summary>
        /// EVERY active blocker, priority order, one per line — so the text
        /// always agrees with whichever cost lines are flushing red.
        /// </summary>
        private string BlockReason(ActionData action) {
            if (!action || Manager.Phase != GamePhase.Weekday) {
                return null;
            }

            var reasons = new List<string>();
            if (!Manager.PrerequisitesMet(action)) {
                reasons.Add("REQUIRES: " + string.Join(", ",
                    Manager.MissingPrerequisites(action).Select(p => p.DisplayName.ToUpperInvariant())));
            }
            if (Manager.People.Value < action.MinSupporters) {
                reasons.Add($"NEEDS {action.MinSupporters:0} SUPPORTERS. {Manager.People.Value:0} ARE WITH US.");
            }
            if (!action.Repeatable && Manager.Queue.Any(e => e.Action == action)) {
                reasons.Add("ALREADY IN TODAY'S QUEUE");
            }
            if (action.TimeCost > Manager.DailyActionPoints) {
                reasons.Add($"NEEDS {action.TimeCost} MAN-HOURS. THE COMMUNE MUSTERS {Manager.DailyActionPoints} A DAY. GROW.");
            } else if (action.TimeCost > Manager.UnreservedHours) {
                reasons.Add("NOT ENOUGH MAN-HOURS LEFT TODAY");
            }
            if (!action.CanAfford) {
                reasons.Add("THE COMMUNE LACKS THE SUPPLIES");
            }
            return reasons.Count == 0 ? null : string.Join("\n", reasons);
        }
    }
}
