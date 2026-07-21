using UnityEngine;
using UnityEngine.UIElements;

namespace RevManager {
    /// <summary>
    /// Bottom panel news feed, the ending overlay, restart, and the shared
    /// button audio hookup.
    ///
    /// See RevGameScreenController.cs for the layout map and field list.
    /// </summary>
    
    public partial class RevGameScreenController {
        private void OnRestartClicked() {
            ResetRunUi();
            Manager?.StartRun();
        }

        /// <summary>
        /// Clears per-run UI (journal, queue cache, ending overlay) so a
        /// restart starts visually clean. Called by the old overlay's restart
        /// button and by the meta screens' Start/Start Another Revolution.
        /// </summary>
        public void ResetRunUi()
        {
            m_NewsTvAutoClose?.Pause();
            m_NewsTvAutoClose = null;

            m_DisplayedNews = null;
            m_NewsTvTimerProgress = 1f;
            m_NewsTvBackgroundByStory.Clear();

            if (m_NewsTvOverlay != null)
            {
                m_NewsTvOverlay.style.display = DisplayStyle.None;
            }

            if (m_NewsTvBackground != null)
            {
                m_NewsTvBackground.style.backgroundImage =
                    new StyleBackground(StyleKeyword.None);
            }

            m_NewsTvCosts?.Clear();
            m_NewsTvTimerRing?.MarkDirtyRepaint();

            m_JournalScroll?.Clear();
            m_ShownQueueVersion = -1;
            m_EndingOverlay?.RemoveFromClassList("ending-overlay--visible");
        }

        /// <summary>
        /// One journal entry = headline + collapsed body. Clicking a headline
        /// toggles the body; if the news carries an UrgentAction it ALSO pulls
        /// that action into the detail card, ready for Add First (which
        /// preempts whatever's running).
        /// Inline styles on urgent/body are placeholders until the classes
        /// (news-entry--actionable, news-entry__body) get styled in the uss.
        /// </summary>
        private void OnJournalUpdated(JournalEntry entry) {
            // The container wears the newsprint strip (.news-entry) so the
            // paper stretches around the body when it expands; headline and
            // body inherit its color/font.

            if (entry.Source != null) AudioManager.Instance?.PlayNews();

            var container = new VisualElement();
            container.AddToClassList("news-entry");
            container.AddToClassList($"news-entry--{entry.Tone.ToString().ToLowerInvariant()}");

            var label = new Label($"W{entry.Week}D{entry.Day}  {entry.Headline}");
            label.AddToClassList("news-entry__headline");
            container.Add(label);

            Label body = null;
            string bodyText = entry.Source ? entry.Source.Body : null;
            if (!string.IsNullOrEmpty(bodyText)) {
                body = new Label(bodyText);
                body.AddToClassList("news-entry__body");
                body.style.display = DisplayStyle.None;
                body.style.whiteSpace = WhiteSpace.Normal;
                body.style.opacity = 0.85f;
                body.style.marginTop = 4;
                container.Add(body);
            }

            if (entry.Source && entry.Tone != NewsTone.Flavor)
            {
                NewsEventData capturedNews = entry.Source;

                container.RegisterCallback<ClickEvent>(_ =>
                {
                    if (capturedNews.Tone == NewsTone.Opening)
                    {
                        MenuScreensController menuScreens = GetComponent<MenuScreensController>();

                        if (menuScreens == null)
                        {
                            menuScreens = FindFirstObjectByType<MenuScreensController>();
                        }

                        if (menuScreens == null)
                        {
                            Debug.LogError("Journal cannot find MenuScreensController.");
                            return;
                        }

                        menuScreens.ShowOpeningNews(capturedNews);
                    }
                    else
                    {
                        ShowNewsTv(capturedNews);
                    }
                    
                });
            }
            else if (body != null)
            {
                bool expanded = false;
                Label capturedBody = body;

                container.RegisterCallback<ClickEvent>(_ =>
                {
                    expanded = !expanded;

                    capturedBody.style.display =
                        expanded ? DisplayStyle.Flex : DisplayStyle.None;
                });
            }

            m_JournalScroll.Add(container);
            m_JournalScroll.schedule.Execute(() => m_JournalScroll.ScrollTo(container));
        }
        
        private void OnGameEnded(EndingData ending) {
            m_EndingTitle.text = ending ? ending.Title : "It's Over";
            m_EndingBody.text = ending ? ending.Body : "No ending matched. Check the EndingBucket has a fallback with open conditions.";

            // The full-art win/lose screens supersede this text card; it only
            // shows as a fallback when the meta screens controller is absent.
            if (!GetComponent<MenuScreensController>()) {
                m_EndingOverlay.AddToClassList("ending-overlay--visible");
            }
        }

        private void OnActionCompleted(ActionData action)
        {
            AudioManager.Instance?.PlayCompleteTask();
        }

        // audio sfx section?

        private static void RegisterButtonAudio(Button button)
        {
            if (button == null)
            {
                return;
            }

            button.RegisterCallback<PointerEnterEvent>(_ =>
            {
                AudioManager.Instance?.PlayHover();
            });

            bool isQueueButton =
                button.name == "add-first-button" ||
                button.name == "add-last-button";

            if (!isQueueButton)
            {
                button.RegisterCallback<ClickEvent>(_ =>
                {
                    AudioManager.Instance?.PlayClick();
                });
            }
        }
    }
}
