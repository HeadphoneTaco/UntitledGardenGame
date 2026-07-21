using UnityEngine;
using UnityEngine.UIElements;

namespace RevManager {
    /// <summary>
    /// Options-screen guts: volume sliders, the cycling font choice
    /// (persisted via PlayerPrefs), and the dev-only force-ending strip on
    /// the pause card.
    ///
    /// See MenuScreensController.cs for screen flow and the field list.
    /// </summary>
    public partial class MenuScreensController {
        [System.Serializable]
        private class FontChoice {
            public string Label = "DEFAULT";
            [Tooltip("Leave empty to use the stylesheet default (Lato-Black). Assign a Font asset (e.g. OpenDyslexic) to make it selectable.")]
            public Font Font;
        }

        [Header("Font options (cycled by the options screen FONT button)")]
        [SerializeField, Tooltip("First entry should be the default (empty Font). Add accessibility fonts here; no code changes needed. Choice persists via PlayerPrefs.")]
        private FontChoice[] m_FontChoices = { new FontChoice() };

        private const string k_FontPrefKey = "rev.fontChoice";
        private int m_FontIndex;
        private VisualElement m_DocumentRoot;
        private Button m_FontButton;

        // ---- Font choice ----

        private void CycleFontChoice() {
            if (m_FontChoices == null || m_FontChoices.Length == 0) {
                return;
            }
            m_FontIndex = (m_FontIndex + 1) % m_FontChoices.Length;
            PlayerPrefs.SetInt(k_FontPrefKey, m_FontIndex);
            ApplyFontChoice();
        }

        private void ApplyFontChoice() {
            if (m_FontChoices == null || m_FontChoices.Length == 0 || m_DocumentRoot == null) {
                return;
            }
            FontChoice choice = m_FontChoices[Mathf.Clamp(m_FontIndex, 0, m_FontChoices.Length - 1)];
            if (choice.Font) {
                m_DocumentRoot.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(choice.Font));
            } else {
                // Clear the inline override; the stylesheet default takes over.
                m_DocumentRoot.style.unityFontDefinition = StyleKeyword.Null;
            }
            if (m_FontButton != null) {
                m_FontButton.text = $"FONT: {choice.Label}";
            }
        }

        // ---- Options / audio ----

        private void BindVolumeSliders(VisualElement root) {
            m_MasterSlider = root.Q<Slider>("volume-master");
            m_MusicSlider = root.Q<Slider>("volume-music");
            m_AmbienceSlider = root.Q<Slider>("volume-ambience");
            m_SfxSlider = root.Q<Slider>("volume-sfx");

            m_MasterSlider?.RegisterValueChangedCallback(evt => AudioListener.volume = evt.newValue);
            m_MusicSlider?.RegisterValueChangedCallback(evt => {
                if (AudioManager.Instance) AudioManager.Instance.MusicVolume = evt.newValue;
            });
            m_AmbienceSlider?.RegisterValueChangedCallback(evt => {
                if (AudioManager.Instance) AudioManager.Instance.AmbienceVolume = evt.newValue;
            });
            m_SfxSlider?.RegisterValueChangedCallback(evt => {
                if (AudioManager.Instance) AudioManager.Instance.SfxVolume = evt.newValue;
            });
        }

        private void SyncSlidersFromAudio() {
            m_MasterSlider?.SetValueWithoutNotify(AudioListener.volume);
            if (!AudioManager.Instance) {
                return;
            }
            m_MusicSlider?.SetValueWithoutNotify(AudioManager.Instance.MusicVolume);
            m_AmbienceSlider?.SetValueWithoutNotify(AudioManager.Instance.AmbienceVolume);
            m_SfxSlider?.SetValueWithoutNotify(AudioManager.Instance.SfxVolume);
        }

        // ---- Debug ending shortcuts (pause card) ----

        /// <summary>
        /// Force-end buttons for basic end-screen testing: each slams the
        /// bars to a spread of community/machine values and ends the run
        /// through the real ending ladder. Stripped from release builds by
        /// hiding the strip outside the editor / dev builds.
        /// </summary>
        private void BindDebugEndButtons(VisualElement root) {
            VisualElement strip = root.Q<VisualElement>("debug-end-strip");
            if (strip == null) {
                return;
            }
            if (!Application.isEditor && !Debug.isDebugBuild) {
                strip.style.display = DisplayStyle.None;
                return;
            }
            Bind(root, "debug-end-win-high", () => ForceEnd(0.9f, 0f));
            Bind(root, "debug-end-win-low", () => ForceEnd(0.15f, 0.05f));
            Bind(root, "debug-end-lose", () => ForceEnd(0.4f, 0.9f));
            Bind(root, "debug-end-collapse", () => ForceEnd(0f, 0.71f));
        }

        private void ForceEnd(float communityProgress, float machineProgress) {
            Manager?.DebugForceEnd(communityProgress, machineProgress);
        }
    }
}
