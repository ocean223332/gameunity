using UnityEngine;

namespace TienTuyen.Presentation.Interface
{
    /// <summary>A full-canvas layer that fades and slides in and out on unscaled time.</summary>
    public abstract class UiScreen
    {
        public RectTransform Root { get; }
        public bool Shown { get; private set; }
        protected readonly CanvasGroup Group;
        /// <summary>The part that slides during transitions (the backdrop stays put).</summary>
        protected RectTransform Motion;
        protected Vector2 SlideFrom = new Vector2(0, -18);
        private float visibility;

        protected UiScreen(string name, Transform parent)
        {
            Root = UiKit.Rect(name, parent);
            Group = Root.gameObject.AddComponent<CanvasGroup>();
            Motion = Root;
            // Stays active while it is being built (TextMeshPro needs Awake for material edits);
            // the owner calls Snap() once every screen exists.
            Group.alpha = 0;
        }

        public void Show(bool show)
        {
            if (show == Shown) return;
            Shown = show;
            if (show)
            {
                Root.gameObject.SetActive(true);
                Root.SetAsLastSibling();
                OnShow();
            }
            Group.interactable = Group.blocksRaycasts = show;
        }

        /// <summary>Advances the transition; returns true while the screen is on-screen.</summary>
        public bool Animate(float deltaTime)
        {
            if (!Shown && visibility <= 0f) return false;
            visibility = Mathf.MoveTowards(visibility, Shown ? 1f : 0f, deltaTime * (Shown ? 4.5f : 7f));
            float eased = 1f - Mathf.Pow(1f - visibility, 3f);
            Group.alpha = eased;
            if (Motion != null) Motion.anchoredPosition = BasePosition + SlideFrom * (1f - eased);
            if (!Shown && visibility <= 0f) Root.gameObject.SetActive(false);
            return true;
        }

        /// <summary>Snap to fully shown or hidden without a transition.</summary>
        public void Snap()
        {
            visibility = Shown ? 1f : 0f;
            Group.alpha = visibility;
            if (Motion != null) Motion.anchoredPosition = BasePosition;
            Root.gameObject.SetActive(Shown);
        }

        protected Vector2 BasePosition { get; set; }
        protected virtual void OnShow() { }
    }
}
