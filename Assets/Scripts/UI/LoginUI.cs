using Cardio.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Cardio.UI
{
    /// <summary>
    /// Login / Register screen.
    ///
    /// PHASE 1 STATUS - IMPORTANT FOR THE REPORT:
    /// Firebase Authentication is NOT implemented yet (it is Phase 7). This
    /// screen is fully built and navigable, and "Continue as Guest" works for
    /// real, but Login and Register deliberately do nothing except explain that
    /// they are not connected. No fake success state is shown, per PSM1
    /// implementation rules 17 and 18.
    ///
    /// When AuthenticationManager lands, only <see cref="OnLogin"/> and
    /// <see cref="OnRegister"/> need to change.
    /// </summary>
    public class LoginUI : MonoBehaviour
    {
        [Header("Fields")]
        [SerializeField] private TMP_InputField emailField;
        [SerializeField] private TMP_InputField passwordField;
        [SerializeField] private TMP_InputField displayNameField;

        [Header("Buttons")]
        [SerializeField] private Button loginButton;
        [SerializeField] private Button registerButton;
        [SerializeField] private Button guestButton;
        [SerializeField] private Button backButton;

        [Header("Status")]
        [SerializeField] private TMP_Text statusLabel;

        private void Start()
        {
            GameManager.Instance?.SetState(GameState.Login);

            if (loginButton != null) loginButton.onClick.AddListener(OnLogin);
            if (registerButton != null) registerButton.onClick.AddListener(OnRegister);
            if (guestButton != null) guestButton.onClick.AddListener(OnGuest);
            if (backButton != null) backButton.onClick.AddListener(() => GameManager.Instance?.GoToMainMenu());

            SetStatus("Firebase Authentication is not connected yet (Phase 7). Use \"Continue as Guest\" to play.");
        }

        private void OnLogin()
        {
            SetStatus("Login is not available yet - AuthenticationManager is scheduled for Phase 7.");
        }

        private void OnRegister()
        {
            SetStatus("Registration is not available yet - AuthenticationManager is scheduled for Phase 7.");
        }

        /// <summary>
        /// Creates a purely local profile. This is the offline path that PSM1
        /// NFR4 requires, so it stays useful even after Firebase is added.
        /// </summary>
        private void OnGuest()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;

            string name = displayNameField != null && !string.IsNullOrWhiteSpace(displayNameField.text)
                ? displayNameField.text.Trim()
                : "Guest";

            gm.StartNewSession("guest", name);

            if (gm.Save != null)
            {
                gm.Save.Progress.LastUserId = "guest";
                gm.Save.Progress.LastDisplayName = name;
                gm.Save.SaveNow();
            }

            gm.GoToMainMenu();
        }

        private void SetStatus(string message)
        {
            if (statusLabel != null) statusLabel.text = message;
        }
    }
}
