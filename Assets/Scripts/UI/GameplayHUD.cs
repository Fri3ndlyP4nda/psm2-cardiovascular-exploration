using Cardio.Core;
using Cardio.DDA;
using Cardio.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Cardio.UI
{
    /// <summary>
    /// In-game HUD: Blood Count, level, difficulty, score, objective board and
    /// the hint indicator.
    ///
    /// It is a pure *view*. It never decides anything - it subscribes to
    /// PlayerHealth and GameManager events and redraws. That is what lets the
    /// DDA work in Phase 4 be a change to one manager instead of a change
    /// scattered through the UI.
    /// </summary>
    public class GameplayHUD : MonoBehaviour
    {
        public static GameplayHUD Instance { get; private set; }

        [Header("Root")]
        [Tooltip("Used to hide the HUD outside gameplay without disabling its children.")]
        [SerializeField] private CanvasGroup canvasGroup;

        [Header("Blood Count")]
        [SerializeField] private Image bloodCountFill;
        [SerializeField] private TMP_Text bloodCountLabel;
        [SerializeField] private Color healthyColor = new Color(0.85f, 0.18f, 0.22f);
        [SerializeField] private Color criticalColor = new Color(0.95f, 0.65f, 0.15f);
        [SerializeField, Range(0f, 1f)] private float criticalThreshold = 0.3f;

        [Header("Session info")]
        [SerializeField] private TMP_Text levelLabel;
        [SerializeField] private TMP_Text difficultyLabel;
        [SerializeField] private TMP_Text scoreLabel;

        [Header("Objective board")]
        [SerializeField] private ObjectiveBoardUI objectiveBoard;

        [Header("Hint indicator")]
        [Tooltip("Shown when the HintManager (Phase 4) offers assistance. Hidden in Phase 1.")]
        [SerializeField] private GameObject hintIndicator;
        [SerializeField] private TMP_Text hintLabel;

        [Header("Interaction prompt")]
        [Tooltip("Shown when the player is near a puzzle station or other interactable.")]
        [SerializeField] private GameObject interactionPrompt;
        [SerializeField] private TMP_Text interactionLabel;

        [Header("Performance readout")]
        [Tooltip("On-screen FPS counter used to verify the PSM1 60 FPS requirement.")]
        [SerializeField] private TMP_Text fpsLabel;
        [SerializeField] private bool showFps = true;

        /// <summary>
        /// What the player is told when a level begins.
        ///
        /// Nothing in the game said this anywhere. The only guidance that existed
        /// was the "[E] Examine" prompt, which only appears once you are already
        /// standing on a station - and you cannot reach one without knowing how to
        /// walk. A study participant handed a laptop for forty minutes should not
        /// have to guess the controls.
        /// </summary>
        /// <summary>Prefix on tier announcements, so the expiry timer can recognise its own.</summary>
        private const string TierAnnouncementPrefix = "Difficulty ";

        private const string ControlsReminder =
            "WASD move   \u00B7   Mouse look   \u00B7   Space jump   \u00B7   E examine   \u00B7   Esc pause";

        [Header("First-run guidance")]
        [Tooltip("Seconds the control reminder stays on screen when a level begins.")]
        [SerializeField, Range(0f, 30f)] private float controlsReminderSeconds = 8f;

        [Header("Damage feedback")]
        [Tooltip("How long the Blood Count bar flashes after a hit.")]
        [SerializeField, Range(0f, 1f)] private float damageFlashSeconds = 0.35f;
        [SerializeField] private Color damageFlashColor = new Color(1f, 0.95f, 0.9f);

        private PlayerHealth _health;

        /// <summary>Colour the bar should settle back to once a flash ends.</summary>
        private Color _restingFillColor;
        private float _damageFlashUntil = -1f;

        /// <summary>Last tier announced, so only real changes are called out.</summary>
        private DifficultyTier _lastAnnouncedTier;
        private bool _hasAnnouncedTier;

        private LevelId _controlsShownForLevel = LevelId.None;
        private float _controlsHideAtTime = -1f;
        private float _fpsAccumulator;
        private int _fpsFrames;
        private float _fpsTimer;

        public ObjectiveBoardUI ObjectiveBoard => objectiveBoard;

        /// <summary>True while the Blood Count bar is flashing from a hit.</summary>
        public bool DamageFlashActive => _damageFlashUntil >= 0f;

        /// <summary>Whatever the hint line currently reads. Empty when nothing is shown.</summary>
        public string CurrentHintText => hintLabel != null ? hintLabel.text : string.Empty;

        private void Awake()
        {
            Instance = this;
            if (hintIndicator != null) hintIndicator.SetActive(false);
            if (interactionPrompt != null) interactionPrompt.SetActive(false);
            if (fpsLabel != null) fpsLabel.gameObject.SetActive(showFps);
        }

        private void OnEnable()
        {
            var gm = GameManager.Instance;
            if (gm != null)
            {
                gm.SessionChanged += OnSessionChanged;
                gm.StateChanged += OnStateChanged;
                OnSessionChanged(gm.Session);
            }

            // The DDA is the centrepiece of this project and, until now, the one
            // thing the player could not perceive at all: the tier label changed
            // quietly in a corner and nothing marked the moment. A study
            // participant cannot report on adaptation they never noticed.
            var dda = Cardio.DDA.DDAManager.Instance;
            if (dda != null) dda.TierChanged += OnTierChanged;
        }

        private void OnDisable()
        {
            var gm = GameManager.Instance;
            if (gm != null)
            {
                gm.SessionChanged -= OnSessionChanged;
                gm.StateChanged -= OnStateChanged;
            }

            var dda = Cardio.DDA.DDAManager.Instance;
            if (dda != null) dda.TierChanged -= OnTierChanged;

            UnbindHealth();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            BindHealth(FindAnyObjectByType<PlayerHealth>());
        }

        private void Update()
        {
            if (showFps && fpsLabel != null) UpdateFpsCounter();
            ExpireControlsReminder();
            UpdateDamageFlash();
        }

        /// <summary>
        /// Flashes the Blood Count bar when the player is hit.
        ///
        /// PlayerHealth has raised Damaged since Phase 1 and only the audio cue and
        /// the metrics tracker ever listened, so losing Blood Count had no visual
        /// signal at all beyond a bar that was already shrinking. A hit the player
        /// does not notice is a hit they cannot learn from - which matters most for
        /// hazards, where the lesson is "do not stand there".
        /// </summary>
        private void OnDamaged(int amount)
        {
            if (amount <= 0 || damageFlashSeconds <= 0f) return;

            _damageFlashUntil = Time.unscaledTime + damageFlashSeconds;
            if (bloodCountFill != null) bloodCountFill.color = damageFlashColor;
        }

        private void UpdateDamageFlash()
        {
            if (_damageFlashUntil < 0f) return;

            if (Time.unscaledTime >= _damageFlashUntil)
            {
                _damageFlashUntil = -1f;
                if (bloodCountFill != null) bloodCountFill.color = _restingFillColor;
                return;
            }

            if (bloodCountFill == null) return;

            // Unscaled, so the flash still resolves if something froze time.
            float remaining = (_damageFlashUntil - Time.unscaledTime) / Mathf.Max(0.0001f, damageFlashSeconds);
            bloodCountFill.color = Color.Lerp(_restingFillColor, damageFlashColor, remaining);
        }

        /// <summary>Announces a difficulty change, with its direction.</summary>
        private void OnTierChanged(DifficultySettings settings)
        {
            if (settings == null) return;

            DifficultyTier tier = settings.Tier;

            // The first call is the session settling on its starting tier, not an
            // adaptation, so it is recorded without being announced.
            if (!_hasAnnouncedTier)
            {
                _hasAnnouncedTier = true;
                _lastAnnouncedTier = tier;
                return;
            }

            if (tier == _lastAnnouncedTier) return;

            bool harder = tier > _lastAnnouncedTier;
            _lastAnnouncedTier = tier;

            ShowHint(harder
                ? $"{TierAnnouncementPrefix}raised to {tier} - you are answering well."
                : $"{TierAnnouncementPrefix}eased to {tier} - take your time.");

            _controlsHideAtTime = Time.unscaledTime + 5f;
        }

        /// <summary>
        /// Takes the reminder down once it has been read - but only if it is still
        /// the thing on screen. A real hint arriving in the meantime wins, and
        /// clearing blindly here would wipe it.
        /// </summary>
        private void ExpireControlsReminder()
        {
            if (_controlsHideAtTime < 0f || Time.unscaledTime < _controlsHideAtTime) return;

            _controlsHideAtTime = -1f;

            // Only clears messages this class put up - the controls reminder and the
            // tier announcement. A hint from HintManager arriving in between wins and
            // must not be wiped by a timer it knows nothing about.
            if (hintLabel == null) return;

            // ClearHint sets the label to null, so this has to tolerate a null
            // string - StartsWith on one throws, and the exception surfaced as
            // unrelated tests failing several classes later.
            string current = hintLabel.text;
            if (string.IsNullOrEmpty(current)) return;

            if (current == ControlsReminder || current.StartsWith(TierAnnouncementPrefix)) ClearHint();
        }

        // ------------------------------------------------------------------
        // Binding
        // ------------------------------------------------------------------

        /// <summary>Attaches the HUD to a player. Called again after a respawn.</summary>
        public void BindHealth(PlayerHealth health)
        {
            if (health == _health) return;

            UnbindHealth();
            _health = health;
            if (_health == null) return;

            _health.BloodCountChanged += OnBloodCountChanged;
            _health.Damaged += OnDamaged;
            OnBloodCountChanged(_health.CurrentBloodCount, _health.MaxBloodCount);
        }

        private void UnbindHealth()
        {
            if (_health == null) return;
            _health.BloodCountChanged -= OnBloodCountChanged;
            _health.Damaged -= OnDamaged;
            _health = null;
        }

        // ------------------------------------------------------------------
        // Event handlers
        // ------------------------------------------------------------------

        private void OnBloodCountChanged(int current, int max)
        {
            float normalised = max <= 0 ? 0f : (float)current / max;

            _restingFillColor = normalised <= criticalThreshold ? criticalColor : healthyColor;

            if (bloodCountFill != null)
            {
                bloodCountFill.fillAmount = normalised;

                // Do not stamp over an in-flight flash; Update restores the resting
                // colour when it finishes.
                if (_damageFlashUntil < 0f) bloodCountFill.color = _restingFillColor;
            }

            if (bloodCountLabel != null) bloodCountLabel.text = $"{current} / {max}";
        }

        private void OnSessionChanged(SessionData session)
        {
            if (session == null) return;

            // Keyed off the level changing rather than the state reaching Playing,
            // because Playing is re-entered every time a puzzle panel closes and
            // the reminder would come back on every question.
            if (session.CurrentLevel != LevelId.None && session.CurrentLevel != _controlsShownForLevel)
            {
                _controlsShownForLevel = session.CurrentLevel;
                if (controlsReminderSeconds > 0f)
                {
                    ShowHint(ControlsReminder);
                    _controlsHideAtTime = Time.unscaledTime + controlsReminderSeconds;
                }
            }

            if (levelLabel != null) levelLabel.text = GameConstants.DisplayNameFor(session.CurrentLevel);
            if (difficultyLabel != null) difficultyLabel.text = $"Difficulty: {session.CurrentDifficulty}";
            if (scoreLabel != null) scoreLabel.text = $"Score: {session.Score}";
        }

        private void OnStateChanged(GameState state)
        {
            // Fade the whole HUD out outside of gameplay so it does not sit on
            // top of the level result panels. A CanvasGroup is used rather than
            // deactivating children, which would wipe the hint indicator's own
            // visibility state.
            if (canvasGroup == null) return;

            bool visible = state == GameState.Playing
                        || state == GameState.Paused
                        || state == GameState.Puzzle;   // objective board stays readable while answering
            canvasGroup.alpha = visible ? 1f : 0f;
        }

        // ------------------------------------------------------------------
        // Public API used by other systems
        // ------------------------------------------------------------------

        /// <summary>Shows or hides the hint indicator. Driven by HintManager from Phase 4.</summary>
        public void ShowHint(string message)
        {
            if (hintLabel != null) hintLabel.text = message;
            if (hintIndicator != null) hintIndicator.SetActive(!string.IsNullOrEmpty(message));
        }

        public void ClearHint() => ShowHint(null);

        /// <summary>Shows the "[E] Examine" style prompt. Driven by PlayerInteraction.</summary>
        public void ShowInteractionPrompt(string message)
        {
            if (interactionLabel != null) interactionLabel.text = message;
            if (interactionPrompt != null) interactionPrompt.SetActive(!string.IsNullOrEmpty(message));
        }

        public void ClearInteractionPrompt() => ShowInteractionPrompt(null);

        private void UpdateFpsCounter()
        {
            _fpsAccumulator += 1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            _fpsFrames++;
            _fpsTimer += Time.unscaledDeltaTime;

            if (_fpsTimer < 0.5f) return;

            int fps = Mathf.RoundToInt(_fpsAccumulator / _fpsFrames);
            fpsLabel.text = $"{fps} FPS";
            fpsLabel.color = fps >= 60 ? new Color(0.6f, 0.85f, 0.6f) : new Color(0.9f, 0.7f, 0.4f);

            _fpsAccumulator = 0f;
            _fpsFrames = 0;
            _fpsTimer = 0f;
        }
    }
}
