using System.Collections;
using UnityEngine;

namespace RevManager
{
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [Header("Audio Sources")]
        [SerializeField] private AudioSource musicSource;
        [SerializeField] private AudioSource ambienceSource;
        [SerializeField] private AudioSource sfxSource;

        [Header("Music")]
        [SerializeField] private AudioClip mainMusic;

        [Header("Rotating Background Ambience")]
        [SerializeField] private AudioClip[] ambienceClips;
        [SerializeField] private float minimumAmbientDelay = 2f;
        [SerializeField] private float maximumAmbientDelay = 6f;

        [Header("UI SFX")]
        [SerializeField] private AudioClip clickClip;
        [SerializeField] private AudioClip[] hoverClips;
        [SerializeField] private AudioClip[] addQueueClips;
        [SerializeField] private AudioClip errorClip;

        [Header("Game SFX")]
        [SerializeField] private AudioClip completeTaskClip;
        [SerializeField] private AudioClip[] newsClips;

        private int _lastAmbienceIndex = -1;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void Start()
        {
            StartMusic();
            if (ambienceClips.Length > 0) StartCoroutine(AmbienceRoutine());
            
        }

        private void StartMusic()
        {
            if (mainMusic == null || musicSource == null) return;
            musicSource.clip = mainMusic;
            musicSource.loop = true;
            musicSource.Play();
        }

        private IEnumerator AmbienceRoutine()
        {
            while (true)
            {
                AudioClip nextClip = GetRandomAmbienceClip();

                if (nextClip != null)
                {
                    ambienceSource.clip = nextClip;
                    ambienceSource.loop = false;
                    ambienceSource.Play();

                    yield return new WaitForSeconds(nextClip.length);
                }

                float delay = Random.Range(minimumAmbientDelay, maximumAmbientDelay);
                yield return new WaitForSeconds(delay);
                
            }
        }

        private AudioClip GetRandomAmbienceClip()
        {
            if (ambienceClips == null || ambienceClips.Length == 0) return null;
            

            if (ambienceClips.Length == 1)
            {
                _lastAmbienceIndex = 0;
                return ambienceClips[0];
            }

            int newIndex;

            do
            {
                newIndex = Random.Range(0, ambienceClips.Length);
            }
            while (newIndex == _lastAmbienceIndex);

            _lastAmbienceIndex = newIndex;
            return ambienceClips[newIndex];
        }

        public void PlayClick()
        {
            PlaySfx(clickClip);
        }

        public void PlayHover()
        {
            PlayRandomSfx(hoverClips);
        }

        public void PlayAddToQueue()
        {
            PlayRandomSfx(addQueueClips);
        }

        public void PlayError()
        {
            PlaySfx(errorClip);
        }

        public void PlayCompleteTask()
        {
            PlaySfx(completeTaskClip);
        }

        public void PlayNews()
        {
            PlayRandomSfx(newsClips);
        }

        private void PlaySfx(AudioClip clip)
        {
            if (clip != null && sfxSource != null)
            {
                sfxSource.PlayOneShot(clip);
            }
        }

        private void PlayRandomSfx(AudioClip[] clips)
        {
            if (clips == null || clips.Length == 0) return;
            AudioClip selectedClip = clips[Random.Range(0, clips.Length)];
            PlaySfx(selectedClip);
        }
    }
}