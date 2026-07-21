using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace RevManager {
    /// <summary>
    /// Drives the meta screens layered over the game in RevGameScreen.uxml:
    /// title, pause, options, credits, and the full-art win/lose end screens.
    ///
    /// Lives on the same GameObject as the UIDocument, next to
    /// RevGameScreenController. All wiring is by element name — the only
    /// Inspector step is adding this component.
    ///
    /// Flow rules (jam requirements: title, pause, end screen, restart, no
    /// soft-locks):
    ///   - Title shows on launch; START begins a fresh run.
    ///   - Esc toggles pause during play and backs out of options/credits.
    ///   - Time.timeScale parks at 0 while any screen is up, so the day
    ///     clock and drain can't advance behind a menu.
    ///   - Game end picks the win or lose art via EndingData.IsVictory and
    ///     prints the specific ladder ending underneath.
    ///   - Options/credits remember which screen opened them, so BACK always
    ///     lands somewhere sensible.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class MenuScreensController : MonoBehaviour {
        private enum MetaScreen {
            None,
            Title,
            Pause,
            Options,
            Credits,
            Win,
            Lose,
            Opening,
        }

        [SerializeField, Tooltip("Show the title screen when the scene loads. Turn off to boot straight into the game while iterating.")]
        private bool m_ShowTitleOnLaunch = true;

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

        private VisualElement m_TitleScreen;
        private VisualElement m_PauseScreen;
        private VisualElement m_OptionsScreen;
        private VisualElement m_CreditsScreen;
        private VisualElement m_WinScreen;
        private VisualElement m_LoseScreen;
        private Label m_WinEndingLabel;
        private Label m_LoseEndingLabel;
        private Slider m_MasterSlider;
        private Slider m_MusicSlider;
        private Slider m_AmbienceSlider;
        private Slider m_SfxSlider;

        private MetaScreen m_Current = MetaScreen.None;
        private MetaScreen m_ReturnTo = MetaScreen.Title;
        private bool m_Subscribed;

        private RevGameScreenController m_GameScreen;
        private static RevGameManager Manager => RevGameManager.Exists ? RevGameManager.Instance : null;
        
        private VisualElement m_OpeningNewsScreen;
        private Label m_OpeningNewsHeadline;
        private Label m_OpeningNewsBody;

        private void OnEnable() {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            m_GameScreen = GetComponent<RevGameScreenController>();
            // Font overrides must land on the .screen element itself: it
            // carries the stylesheet's font rule, and a stylesheet rule on an
            // element beats anything inherited from higher up. Inline style on
            // the same element beats the stylesheet, and children inherit it.
            m_DocumentRoot = root.Q<VisualElement>("root") ?? root;

            m_TitleScreen = root.Q<VisualElement>("title-screen");
            m_PauseScreen = root.Q<VisualElement>("pause-screen");
            m_OptionsScreen = root.Q<VisualElement>("options-screen");
            m_CreditsScreen = root.Q<VisualElement>("credits-screen");
            m_WinScreen = root.Q<VisualElement>("win-screen");
            m_LoseScreen = root.Q<VisualElement>("lose-screen");
            m_WinEndingLabel = root.Q<Label>("win-ending-label");
            m_LoseEndingLabel = root.Q<Label>("lose-ending-label");
            
            m_OpeningNewsScreen = root.Q<VisualElement>("opening-news-screen");
            m_OpeningNewsHeadline = root.Q<Label>("opening-news-headline");
            m_OpeningNewsBody = root.Q<Label>("opening-news-body");

            Bind(root, "opening-news-continue", CloseOpeningNews);
            Bind(root, "title-start-button", StartFreshRun);
            Bind(root, "title-options-button", OpenOptions);
            Bind(root, "title-credits-button", OpenCredits);
            Bind(root, "title-quit-button", QuitGame);

            Bind(root, "pause-resume-button", Resume);
            Bind(root, "pause-options-button", OpenOptions);
            Bind(root, "pause-title-button", ReturnToTitle);
            Bind(root, "pause-quit-button", QuitGame);

            Bind(root, "options-back-button", CloseSubScreen);
            Bind(root, "credits-back-button", CloseSubScreen);

            Bind(root, "win-restart-button", StartFreshRun);
            Bind(root, "win-title-button", ReturnToTitle);
            Bind(root, "win-credits-button", OpenCredits);
            Bind(root, "lose-restart-button", StartFreshRun);
            Bind(root, "lose-title-button", ReturnToTitle);
            Bind(root, "lose-credits-button", OpenCredits);

            BindVolumeSliders(root);
            BindDebugEndButtons(root);
            HideQuitButtonsOnWeb(root);

            // Font choice: cycled from the options screen, persisted, applied
            // as an inline style on the document root so everything inherits.
            m_FontButton = root.Q<Button>("options-font-button");
            Bind(root, "options-font-button", CycleFontChoice);
            m_FontIndex = Mathf.Clamp(PlayerPrefs.GetInt(k_FontPrefKey, 0), 0, Mathf.Max(0, m_FontChoices.Length - 1));
            ApplyFontChoice();
        }

        private void Start() {
            if (m_ShowTitleOnLaunch) {
                Show(MetaScreen.Title);
            }
        }

        private void OnDisable() {
            if (m_Subscribed && RevGameManager.Exists) {
                RevGameManager.Instance.GameEnded -= OnGameEnded;
            }
            m_Subscribed = false;
            Time.timeScale = 1f;
        }

        private void Update() {
            // Manager may not exist yet on the first frames (script order).
            if (!m_Subscribed && Manager != null) {
                Manager.GameEnded += OnGameEnded;
                m_Subscribed = true;
            }
            HandleEscape();
        }

        // ---- Screen switching ----

        /// <summary>
        /// One screen at a time; timeScale 0 whenever any screen is up. The
        /// day clock ticks on scaled time, so nothing advances behind a menu.
        /// </summary>
        private void Show(MetaScreen screen) {
            m_Current = screen;
            // Options opened from the title keeps the title art underneath —
            // otherwise the dim overlay would expose the in-game UI before
            // the player has even pressed START.
            bool titleUnderOptions = screen == MetaScreen.Options && m_ReturnTo == MetaScreen.Title;
            Toggle(m_TitleScreen, screen == MetaScreen.Title || titleUnderOptions);
            Toggle(m_PauseScreen, screen == MetaScreen.Pause);
            Toggle(m_OptionsScreen, screen == MetaScreen.Options);
            Toggle(m_CreditsScreen, screen == MetaScreen.Credits);
            Toggle(m_WinScreen, screen == MetaScreen.Win);
            Toggle(m_LoseScreen, screen == MetaScreen.Lose);
            Toggle(m_OpeningNewsScreen, screen == MetaScreen.Opening);
            Time.timeScale = screen == MetaScreen.None ? 1f : 0f;
        }

        private static void Toggle(VisualElement screen, bool visible) {
            screen?.EnableInClassList("meta-screen--visible", visible);
        }

        private void HandleEscape() {
            if (Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame) {
                return;
            }

            switch (m_Current) {
                case MetaScreen.None:
                    Show(MetaScreen.Pause);
                    break;
                case MetaScreen.Pause:
                    Resume();
                    break;
                case MetaScreen.Options:
                case MetaScreen.Credits:
                    CloseSubScreen();
                    break;
                // Title / Win / Lose ignore Esc: every exit from those is an
                // explicit button, so there's no state to soft-lock into.
            }
        }

        // ---- Button handlers ----

        private void StartFreshRun()
        {
            m_GameScreen?.ResetRunUi();
            Manager?.StartRun();
            ShowOpeningNews();
        }
        
        public void ShowOpeningNews(NewsEventData news = null)
        {
            if (!news)
            {
                news = Manager?.OpeningNews;
            }

            if (!news)
            {
                Show(MetaScreen.None);
                return;
            }

            m_OpeningNewsHeadline.text = news.Headline;
            m_OpeningNewsBody.text = news.Body;
            Show(MetaScreen.Opening);
        }

        private void CloseOpeningNews()
        {
            Show(MetaScreen.None);
        }

        private void Resume() {
            Show(MetaScreen.None);
        }

        private void ReturnToTitle() {
            // The finished/paused run stays parked behind the title (timeScale
            // 0); START always begins a fresh run anyway.
            Show(MetaScreen.Title);
        }

        private void OpenOptions() {
            m_ReturnTo = m_Current;
            SyncSlidersFromAudio();
            Show(MetaScreen.Options);
        }

        private void OpenCredits() {
            m_ReturnTo = m_Current;
            Show(MetaScreen.Credits);
        }

        private void CloseSubScreen() {
            Show(m_ReturnTo);
        }

        private static void QuitGame() {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ---- Game end ----

        private void OnGameEnded(EndingData ending) {
            bool won = ending && ending.IsVictory;
            Label detail = won ? m_WinEndingLabel : m_LoseEndingLabel;
            if (detail != null) {
                detail.text = ending ? $"{ending.Title} — {ending.Body}" : "";
            }
            Show(won ? MetaScreen.Win : MetaScreen.Lose);
        }

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

        // ---- Helpers ----

        private static void Bind(VisualElement root, string buttonName, System.Action onClick) {
            Button button = root.Q<Button>(buttonName);
            if (button != null) {
                button.clicked += onClick;
            }
        }

        /// <summary>Quit is meaningless in a browser; hide it there.</summary>
        private static void HideQuitButtonsOnWeb(VisualElement root) {
            if (Application.platform != RuntimePlatform.WebGLPlayer) {
                return;
            }
            SetDisplay(root.Q<Button>("title-quit-button"), false);
            SetDisplay(root.Q<Button>("pause-quit-button"), false);
        }

        private static void SetDisplay(VisualElement element, bool visible) {
            if (element != null) {
                element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }
    }
}
