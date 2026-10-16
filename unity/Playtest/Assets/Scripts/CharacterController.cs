using UnityEngine;
using UnityEngine.UI;

namespace Playtest
{
    public sealed class CharacterController : MonoBehaviour
    {
        Image image;
        Animator animator;
        string idleState;
        string current;
        float cueSeconds;
        float returnAt;
        bool returning;
        Color baseColor;
        PresentationConfig presentation;
        float width;
        float height;

        public float Width
        {
            get { return width; }
        }

        public float Height
        {
            get { return height; }
        }

        public void Bind(PresentationConfig source, ActorVisual visual, RuntimeAnimatorController controller, byte[] png)
        {
            presentation = source;
            idleState = source.IdleState;
            cueSeconds = source.CueSeconds;
            width = visual.Width;
            height = visual.Height;
            Sprite picture = FromPng(png);
            baseColor = picture == null ? visual.Color : Color.white;
            image = gameObject.GetComponent<Image>();
            if (image == null)
                image = gameObject.AddComponent<Image>();
            image.sprite = picture == null ? Rectangle(visual) : picture;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.color = baseColor;
            animator = gameObject.GetComponent<Animator>();
            if (animator == null)
                animator = gameObject.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            Show(idleState);
        }

        public void Pin(Vector2 anchor, Vector2 anchoredPosition)
        {
            RectTransform rect = transform as RectTransform;
            if (rect == null)
                return;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(width, height);
        }

        public void SetPicture(byte[] png)
        {
            Sprite picture = FromPng(png);
            if (picture == null || image == null)
                return;
            image.sprite = picture;
            baseColor = Color.white;
            Show(current);
        }

        public void Cue(string state)
        {
            Show(state);
            if (state == idleState)
            {
                returning = false;
                return;
            }
            returning = true;
            returnAt = Time.unscaledTime + cueSeconds;
        }

        void Update()
        {
            if (!returning || Time.unscaledTime < returnAt)
                return;
            returning = false;
            Show(idleState);
        }

        void Show(string state)
        {
            current = state;
            if (animator != null && animator.runtimeAnimatorController != null && !string.IsNullOrEmpty(state))
                animator.Play(state, 0, 0f);
            float scale = presentation == null ? 1f : presentation.PoseScale(state);
            transform.localScale = new Vector3(scale, scale, 1f);
            if (image == null)
                return;
            bool hurt = presentation != null && state == presentation.PurchaseEnemy;
            image.color = hurt ? presentation.Critical : baseColor;
        }

        Sprite FromPng(byte[] png)
        {
            if (png == null || png.Length == 0 || presentation == null || presentation.PixelsPerUnit <= 0f)
                return null;
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(png))
                return null;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            return Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0f),
                presentation.PixelsPerUnit);
        }

        Sprite Rectangle(ActorVisual visual)
        {
            Texture2D texture = new Texture2D(visual.Width, visual.Height, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[visual.Width * visual.Height];
            for (int index = 0; index < pixels.Length; index++)
                pixels[index] = Color.white;
            texture.SetPixels(pixels);
            texture.filterMode = FilterMode.Point;
            texture.Apply();
            float pixelsPerUnit = presentation == null || presentation.PixelsPerUnit <= 0f
                ? 1f
                : presentation.PixelsPerUnit;
            return Sprite.Create(
                texture,
                new Rect(0f, 0f, visual.Width, visual.Height),
                new Vector2(0.5f, 0f),
                pixelsPerUnit);
        }
    }
}
