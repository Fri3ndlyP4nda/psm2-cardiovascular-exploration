using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Cardio.UI
{
    /// <summary>
    /// The anatomical label chip the player drags out of the puzzle panel and
    /// drops onto a structure in the 3D scene.
    ///
    /// It does not move the real chip: it drags a lightweight ghost and snaps
    /// the original back, so a failed drop leaves the panel exactly as it was
    /// and the chip can be tried again without any reset logic.
    ///
    /// The ghost has raycastTarget disabled, otherwise it would sit under the
    /// pointer and make every drop look like it landed on UI.
    /// </summary>
    public class DraggableLabel : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] private TMP_Text label;
        [SerializeField] private CanvasGroup canvasGroup;

        private Canvas _canvas;
        private RectTransform _ghost;
        private PuzzleUI _owner;

        /// <summary>Text carried by this chip. For sequence puzzles this is the step name.</summary>
        public string Text
        {
            get => label != null ? label.text : string.Empty;
            set { if (label != null) label.text = value; }
        }

        public void Initialise(PuzzleUI owner, string text)
        {
            _owner = owner;
            Text = text;
            _canvas = GetComponentInParent<Canvas>();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (_owner == null || _canvas == null) return;

            _ghost = CreateGhost();
            if (canvasGroup != null) canvasGroup.alpha = 0.35f;

            _owner.OnLabelDragBegin(this);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_ghost == null) return;

            // Screen-space overlay canvases take raw pointer coordinates.
            _ghost.position = eventData.position;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (_ghost != null)
            {
                Destroy(_ghost.gameObject);
                _ghost = null;
            }

            if (canvasGroup != null) canvasGroup.alpha = 1f;

            _owner?.OnLabelDropped(this, eventData.position);
        }

        /// <summary>Clones the chip's visuals to follow the pointer.</summary>
        private RectTransform CreateGhost()
        {
            var go = new GameObject("DragGhost", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_canvas.transform, false);
            go.transform.SetAsLastSibling();

            var rect = (RectTransform)go.transform;
            rect.sizeDelta = ((RectTransform)transform).sizeDelta;

            var image = go.GetComponent<Image>();
            var source = GetComponent<Image>();
            if (source != null)
            {
                image.sprite = source.sprite;
                image.type = source.type;
                image.color = source.color;
            }
            image.raycastTarget = false;

            var text = new GameObject("Text", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            text.transform.SetParent(go.transform, false);
            var textRect = (RectTransform)text.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            if (label != null)
            {
                text.font = label.font;
                text.fontSize = label.fontSize;
                text.color = label.color;
                text.alignment = label.alignment;
            }
            text.text = Text;
            text.raycastTarget = false;

            return rect;
        }
    }
}
