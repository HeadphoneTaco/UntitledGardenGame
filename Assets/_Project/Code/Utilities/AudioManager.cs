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

        // Volume access for the options screen. Master volume lives on
        // AudioListener there; these cover the three sources.
        public float MusicVolume {
            get => musicSource ? musicSource.volume : 1f;
            set { if (musicSource) musicSource.volume = value; }
        }

        public float AmbienceVolume {
            get => ambienceSource ? ambienceSource.volume : 1f;
            set { if (ambienceSource) ambienceSource.volume = value; }
        }

        public float SfxVolume {
            get => sfxSource ? sfxSource.volume : 1f;
            set { if (sfxSource) sfxSource.volume = value; }
        }

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
            PlayRandomSfx(hoverClips, 0.92f, 1.08f);
        }

        public void PlayAddToQueue()
        {
            PlayRandomSfx(addQueueClips, 0.94f, 1.06f);
        }

        public void PlayNews()
        {
            PlayRandomSfx(newsClips, 0.90f, 1.10f);
        }

        public void PlayError()
        {
            PlaySfx(errorClip);
        }

        public void PlayCompleteTask()
        {
            PlaySfx(completeTaskClip);
        }

        private void PlaySfx(
            AudioClip clip,
            float minimumPitch = 1f,
            float maximumPitch = 1f)
        {
            if (clip == null || sfxSource == null)
            {
                return;
            }

            sfxSource.pitch = Random.Range(
                minimumPitch,
                maximumPitch
            );

            sfxSource.PlayOneShot(clip);
        }

        private void PlayRandomSfx(
            AudioClip[] clips,
            float minimumPitch = 1f,
            float maximumPitch = 1f)
        {
            if (clips == null || clips.Length == 0)
            {
                return;
            }

            AudioClip selectedClip = clips[
                Random.Range(0, clips.Length)
            ];

            PlaySfx(
                selectedClip,
                minimumPitch,
                maximumPitch
            );
        }
    }
}