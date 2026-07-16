using System;
using CoreUtils.GameVariables;

namespace RevManager {
    /// <summary>
    /// A change applied to any GameVariable (Community, Machine, Food, People, etc).
    /// Designers point these at variable assets in the Inspector, so any action or
    /// event can affect any stat without code changes.
    /// </summary>
    [Serializable]
    public struct VariableEffect {
        public GameVariableFloat Variable;
        public float Delta;

        public void Apply() {
            if (Variable) {
                Variable.Value += Delta;
            }
        }
    }

    /// <summary>
    /// A cost that must be affordable before something runs, then gets spent.
    /// </summary>
    [Serializable]
    public struct VariableCost {
        public GameVariableFloat Variable;
        public float Amount;

        public bool CanAfford => !Variable || Variable.Value >= Amount;

        public void Pay() {
            if (Variable) {
                Variable.Value -= Amount;
            }
        }
    }

    public static class VariableEffectUtils {
        public static bool CanAffordAll(this VariableCost[] costs) {
            if (costs == null) {
                return true;
            }
            foreach (VariableCost cost in costs) {
                if (!cost.CanAfford) {
                    return false;
                }
            }
            return true;
        }

        public static void PayAll(this VariableCost[] costs) {
            if (costs == null) {
                return;
            }
            foreach (VariableCost cost in costs) {
                cost.Pay();
            }
        }

        public static void ApplyAll(this VariableEffect[] effects) {
            if (effects == null) {
                return;
            }
            foreach (VariableEffect effect in effects) {
                effect.Apply();
            }
        }
    }
}
