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
                return;
            }

            m_SelectedCostLines.Clear();
            AddDetailLine(m_DetailCosts, $"{action.TimeCost} MAN-HOURS");
            if (action.MinSupporters > 0f) {
                // A requirement, not a cost: nothing is spent, but it gives
                // tier actions a visible goal to push toward.
                AddDetailLine(m_DetailCosts, $"NEEDS {action.MinSupporters:0} SUPPORTERS");
            }
            if (action.Costs != null) {
                foreach (VariableCost cost in action.Costs) {
                    if (cost.Variable) {
                        Label line = AddDetailLine(m_DetailCosts, $"x{cost.Amount:0} {cost.Variable.Name.ToUpperInvariant()}");
                        m_SelectedCostLines.Add((line, cost));
                    }
                }
            }

            if (action.Effects != null) {
                foreach (VariableEffect effect in action.Effects) {
                    if (effect.Variable) {
                        AddDetailLine(m_DetailGains, $"{effect.Delta:+0;-0} {effect.Variable.Name.ToUpperInvariant()}");
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

        /// <summary>
        /// Why the selected action can't be queued right now. Null = it can.
        /// Ordered by what the player should fix first.
        /// </summary>
        private string BlockReason(ActionData action) {
            if (!action || Manager.Phase != GamePhase.Weekday) {
                return null;
            }
            if (!Manager.PrerequisitesMet(action)) {
                return "REQUIRES: " + string.Join(", ",
                    Manager.MissingPrerequisites(action).Select(p => p.DisplayName.ToUpperInvariant()));
            }
            if (Manager.People.Value < action.MinSupporters) {
                return $"NEEDS {action.MinSupporters:0} SUPPORTERS. {Manager.People.Value:0} ARE WITH US.";
            }
            if (!action.Repeatable && Manager.Queue.Any(e => e.Action == action)) {
                return "ALREADY IN TODAY'S QUEUE";
            }
            if (action.TimeCost > Manager.DailyActionPoints) {
                return $"NEEDS {action.TimeCost} MAN-HOURS. THE COMMUNE MUSTERS {Manager.DailyActionPoints} A DAY. GROW.";
            }
            if (action.TimeCost > Manager.UnreservedHours) {
                return "NOT ENOUGH MAN-HOURS LEFT TODAY";
            }
            if (!action.CanAfford) {
                return "THE COMMUNE LACKS THE SUPPLIES";
            }
            return null;
        }
    }
}
