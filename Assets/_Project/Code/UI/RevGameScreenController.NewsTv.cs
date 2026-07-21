using UnityEngine;
using UnityEngine.UIElements;

namespace RevManager {
    /// <summary>
    /// The news TV overlay: daily Important broadcasts (30s auto-close),
    /// weekly Crisis attend/ignore with the countdown ring, and the handoff
    /// back to the manager when a broadcast closes on the weekend.
    ///
    /// See RevGameScreenController.cs for the layout map and field list.
    /// </summary>
    public partial class RevGameScreenController {
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
    }
}
