using Nova;
using UnityEngine;

namespace NovaSamples.UIControls
{
    /// <summary>
    /// Lets a UIBlock's width and height be edited from the inspector or changed from UnityEvents.
    /// </summary>
    [ExecuteAlways]
    public class UIBlockSizeController : MonoBehaviour
    {
        [Tooltip("The UIBlock to resize. If empty, this script uses the UIBlock on the same GameObject.")]
        public UIBlock Target = null;

        [Tooltip("Width to apply to the target UIBlock.")]
        public float Width = 500;

        [Tooltip("Height to apply to the target UIBlock.")]
        public float Height = 300;

        [Tooltip("Apply Width and Height when the scene starts.")]
        public bool ApplyOnStart = true;

        [Tooltip("Apply Width and Height when values change in the inspector.")]
        public bool ApplyInInspector = false;

        [Tooltip("Disable AutoSize on X and Y before applying the manual size.")]
        public bool DisableAutoSize = true;

        [Tooltip("Disable aspect ratio locking before applying the manual size.")]
        public bool DisableAspectRatioLock = true;

        private void Reset()
        {
            Target = GetComponent<UIBlock>();
        }

        private void Start()
        {
            if (Application.isPlaying && ApplyOnStart)
            {
                ApplySize();
            }
        }

        private void OnValidate()
        {
            Width = Mathf.Max(0, Width);
            Height = Mathf.Max(0, Height);

            if (ApplyInInspector)
            {
                ApplySize();
            }
        }

        public void ApplySize()
        {
            SetSize(Width, Height);
        }

        public void SetSize(float width, float height)
        {
            UIBlock target = ResolveTarget();

            if (target == null)
            {
                return;
            }

            if (DisableAutoSize)
            {
                target.AutoSize.X = AutoSize.None;
                target.AutoSize.Y = AutoSize.None;
            }

            if (DisableAspectRatioLock)
            {
                target.AspectRatioAxis = Axis.None;
            }

            Width = Mathf.Max(0, width);
            Height = Mathf.Max(0, height);

            Vector3 size = target.Size.Value;
            size.x = Width;
            size.y = Height;
            target.Size.Value = size;

            target.CalculateLayout();
        }

        public void SetWidth(float width)
        {
            SetSize(width, Height);
        }

        public void SetHeight(float height)
        {
            SetSize(Width, height);
        }

        public void AddSize(Vector2 delta)
        {
            SetSize(Width + delta.x, Height + delta.y);
        }

        public void AddWidth(float delta)
        {
            SetWidth(Width + delta);
        }

        public void AddHeight(float delta)
        {
            SetHeight(Height + delta);
        }

        private UIBlock ResolveTarget()
        {
            if (Target == null)
            {
                Target = GetComponent<UIBlock>();
            }

            return Target;
        }
    }
}
