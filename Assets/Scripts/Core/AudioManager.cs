using System.Collections.Generic;
using UnityEngine;

namespace Cardio.Core
{
    /// <summary>The game's sound cues. One enum value per event worth hearing.</summary>
    public enum AudioCue
    {
        Correct = 0,
        Wrong = 1,
        Hint = 2,
        Damage = 3,
        LevelComplete = 4,
        Click = 5
    }

    /// <summary>
    /// Plays short feedback sounds.
    ///
    /// The clips are procedurally generated WAV files (see AudioFactory) rather
    /// than authored audio, for the same reason the scenes and materials are
    /// generated: the project ships no binary assets it cannot rebuild from
    /// source. They are placeholder tones and are honest about being so - this
    /// is feedback timing, not sound design.
    ///
    /// Clips are loaded from Resources rather than wired into a scene, so any
    /// scene can make a sound without carrying a reference, and a missing clip
    /// degrades to silence instead of a null reference.
    ///
    /// Master volume is not handled here: SettingsPanel already drives
    /// AudioListener.volume, which scales everything this plays.
    /// </summary>
    [DisallowMultipleComponent]
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        private const string ResourceFolder = "Audio/";

        [Header("Mix")]
        [Range(0f, 1f)]
        [SerializeField] private float effectVolume = 0.6f;

        [Tooltip("Turn off to silence all cues without touching the master volume.")]
        [SerializeField] private bool cuesEnabled = true;

        private AudioSource _source;
        private AudioClip[] _clips;

        /// <summary>Cues played since load. Lets a test assert a sound happened without listening.</summary>
        public int CuesPlayed { get; private set; }

        /// <summary>The most recent cue, or null if nothing has played.</summary>
        public AudioCue? LastCue { get; private set; }

        private readonly Dictionary<AudioCue, int> _cueCounts = new Dictionary<AudioCue, int>();

        /// <summary>
        /// How many times one cue has played.
        ///
        /// Asserting on this rather than on LastCue matters: several cues can
        /// fire in the same frame (answering correctly can also bank a hint),
        /// so "the last cue was X" is a race, while "X happened" is not.
        /// </summary>
        public int CueCount(AudioCue cue) => _cueCounts.TryGetValue(cue, out int n) ? n : 0;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;

            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;   // 2D: these are interface sounds, not world sounds

            LoadClips();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void LoadClips()
        {
            var values = (AudioCue[])System.Enum.GetValues(typeof(AudioCue));
            _clips = new AudioClip[values.Length];

            foreach (AudioCue cue in values)
            {
                _clips[(int)cue] = Resources.Load<AudioClip>(ResourceFolder + cue);
            }
        }

        /// <summary>Plays one cue. Silently does nothing if its clip is missing.</summary>
        public void Play(AudioCue cue)
        {
            CuesPlayed++;
            LastCue = cue;
            _cueCounts[cue] = CueCount(cue) + 1;

            if (!cuesEnabled || _source == null || _clips == null) return;

            int index = (int)cue;
            if (index < 0 || index >= _clips.Length) return;

            AudioClip clip = _clips[index];
            if (clip == null) return;

            _source.PlayOneShot(clip, effectVolume);
        }

        /// <summary>Convenience for callers that may run before the bootstrap.</summary>
        public static void PlayCue(AudioCue cue) => Instance?.Play(cue);
    }
}
