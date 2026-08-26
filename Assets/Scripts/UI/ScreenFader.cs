using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Cardio.UI
{
    /// <summary>
    /// Full screen fade used between scenes.
    ///
    /// Self-creating: the first call builds its own canvas and marks it
    /// DontDestroyOnLoad, so no scene needs to contain a fader object and a
    /// missing prefab can never break a scene transition.
    ///
    /// All timing uses unscaled time because fades run while Time.timeScale
    /// is 0 (e.g. quitting to the menu from the pause screen).
    /// </summary>
    [DisallowMultipleComponent]
    public class ScreenFader : MonoBehaviour
    {
        private const int SortingOrder = 5000; // above every gameplay canvas

        private static ScreenFader _instance;
        private CanvasGroup _group;

        private static ScreenFader Instance
        {
            get
            {
                if (_instance != null) return _instance;

                var go = new GameObject("[Screen Fader]");
                DontDestroyOnLoad(go);

                var canvas = go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = SortingOrder;

                var group = go.AddComponent<CanvasGroup>();
                group.alpha = 0f;
                group.blocksRaycasts = false;
                group.interactable = false;

                var imageGo = new GameObject("Overlay", typeof(RectTransform));
                imageGo.transform.SetParent(go.transform, false);
                var rect = imageGo.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;

                var image = imageGo.AddComponent<Image>();
                image.color = Color.black;
                image.raycastTarget = false;

                _instance = go.AddComponent<ScreenFader>();
                _instance._group = group;
                return _instance;
            }
        }

        /// <summary>Fades to black. Yield on this from a coroutine.</summary>
        public static IEnumerator FadeOut(float duration) => Instance.FadeTo(1f, duration);

        /// <summary>Fades back to the game. Yield on this from a coroutine.</summary>
        public static IEnumerator FadeIn(float duration) => Instance.FadeTo(0f, duration);

        /// <summary>Sets the overlay instantly (no coroutine needed).</summary>
        public static void SetAlpha(float alpha)
        {
            Instance._group.alpha = Mathf.Clamp01(alpha);
            Instance._group.blocksRaycasts = Instance._group.alpha > 0.99f;
        }

        private IEnumerator FadeTo(float targetAlpha, float duration)
        {
            // Block clicks while the screen is covered so the player cannot hit
            // a button on a scene that is halfway through being replaced.
            _group.blocksRaycasts = targetAlpha > 0.01f;

            if (duration <= 0f)
            {
                _group.alpha = targetAlpha;
                _group.blocksRaycasts = targetAlpha > 0.99f;
                yield break;
            }

            float start = _group.alpha;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                _group.alpha = Mathf.Lerp(start, targetAlpha, elapsed / duration);
                yield return null;
            }

            _group.alpha = targetAlpha;
            _group.blocksRaycasts = targetAlpha > 0.99f;
        }
    }
}
