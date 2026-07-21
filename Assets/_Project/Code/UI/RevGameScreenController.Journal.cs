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
        
        private void OnNewsFired(NewsEventData news)
        {
            ShowNewsTv(news, true);
        }

        private void ShowNewsTv(NewsEventData news, bool autoClose = false)
        {
            
            if (!news || news.Tone == NewsTone.Opening)
            {
                return;
            }
            
            if (!news || news.Tone == NewsTone.Flavor)
            {
                return;
            }

            m_DisplayedNews = news;
            
            Texture2D containerArt =
                news.Tone == NewsTone.Crisis
                    ? m_NewsTvCrisisContainer
                    : m_NewsTvImportantContainer;

            if (containerArt)
            {
                m_NewsTvPaper.style.backgroundImage =
                    new StyleBackground(containerArt);
            }
            
            if (m_NewsTvBackgrounds != null &&
                m_NewsTvBackgrounds.Length > 0)
            {
                if (!m_NewsTvBackgroundByStory.TryGetValue(news, out Texture2D background))
                {
                    background = m_NewsTvBackgrounds[
                        Random.Range(0, m_NewsTvBackgrounds.Length)];

                    m_NewsTvBackgroundByStory.Add(news, background);
                }

                m_NewsTvBackground.style.backgroundImage =
                    new StyleBackground(background);
            }

            bool activeCrisis =
                news.Tone == NewsTone.Crisis &&
                Manager.PendingCrisis == news;

            m_NewsTvHeadline.text = news.Headline;
            m_NewsTvBody.text = news.Body;

            m_NewsTvCrisisArea.style.display =
                activeCrisis ? DisplayStyle.Flex : DisplayStyle.None;

            m_NewsTvContinue.style.display =
                activeCrisis ? DisplayStyle.None : DisplayStyle.Flex;

            m_NewsTvCosts.Clear();

            if (activeCrisis && news.AttendCosts != null)
            {
                foreach (VariableCost cost in news.AttendCosts)
                {
                    string resourceName =
                        cost.Variable ? cost.Variable.name : "Resource";

                    var costLabel = new Label(
                        $"{resourceName}: {cost.Amount:0}");

                    m_NewsTvCosts.Add(costLabel);
                }
            }

            m_NewsTvAttend.SetEnabled(activeCrisis && Manager.CanAttendPendingCrisis);
            m_NewsTvOverlay.style.display = DisplayStyle.Flex;
            
            m_NewsTvAutoClose?.Pause();
            m_NewsTvAutoClose = null;

            if (autoClose && news.Tone == NewsTone.Important)
            {
                m_NewsTvAutoClose = m_NewsTvOverlay.schedule
                    .Execute(CloseNewsTv)
                    .StartingIn(30000);
            }
            
        }
        
        private void CloseNewsTv()
        {
            m_NewsTvAutoClose?.Pause();
            m_NewsTvAutoClose = null;
            
            NewsEventData closingNews = m_DisplayedNews;
            m_DisplayedNews = null;

            m_NewsTvOverlay.style.display = DisplayStyle.None;

            if (!closingNews || Manager == null)
            {
                return;
            }

            // Friday Important news closes, then the weekly Crisis opens.
            if (closingNews.Tone == NewsTone.Important &&
                Manager.Phase == GamePhase.Weekend)
            {
                Manager.OpenWeeklyCrisis();
                return;
            }

            // Closing, attending, or ignoring the Crisis begins the next week.
            if (closingNews.Tone == NewsTone.Crisis &&
                Manager.Phase == GamePhase.Weekend)
            {
                Manager.BeginNextWeek();
            }
        }
        
        private void AttendNewsTvCrisis()
        {
            Manager.AttendPendingCrisis();

            if (!Manager.HasPendingCrisis)
            {
                CloseNewsTv();
            }
        }

        private void IgnoreNewsTvCrisis()
        {
            Manager.IgnorePendingCrisis();
            CloseNewsTv();
        }
        
        private void DrawCrisisTimerRing(MeshGenerationContext context)
        {
            Rect bounds = m_NewsTvTimerRing.contentRect;
            float radius = Mathf.Min(bounds.width, bounds.height) * 0.5f - 7f;

            if (radius <= 0f)
            {
                return;
            }

            Vector2 center = bounds.center;
            Painter2D painter = context.painter2D;

            painter.lineWidth = 8f;
            painter.lineCap = LineCap.Round;

            // Empty/background ring.
            painter.strokeColor = new Color(0.2f, 0.18f, 0.15f, 0.3f);
            painter.BeginPath();
            painter.Arc(
                center,
                radius,
                new Angle(0f),
                new Angle(359.9f),
                ArcDirection.Clockwise);
            painter.Stroke();

            // Remaining time.
            if (m_NewsTvTimerProgress > 0f)
            {
                painter.strokeColor = new Color(0.75f, 0.08f, 0.12f, 1f);
                painter.BeginPath();
                painter.Arc(
                    center,
                    radius,
                    new Angle(-90f),
                    new Angle(-90f + 359.9f * m_NewsTvTimerProgress),
                    ArcDirection.Clockwise);
                painter.Stroke();
            }
        }
        
        private void OnCrisisResolved(
            NewsEventData crisis,
            bool attended)
        {
            if (m_DisplayedNews != crisis)
            {
                return;
            }

            m_NewsTvTimerProgress = 0f;
            m_NewsTvTimerRing.MarkDirtyRepaint();

            m_NewsTvCrisisArea.style.display =
                DisplayStyle.None;

            m_NewsTvContinue.style.display =
                DisplayStyle.Flex;
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
