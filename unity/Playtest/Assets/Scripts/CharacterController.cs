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

        public void Bind(PresentationConfig source, ActorVisual visual, RuntimeAnimatorController controller, byte[] png)
        {
            presentation = source;
            idleState = source.IdleState;
            cueSeconds = source.CueSeconds;
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

        public void Fit(float scale)
        {
            RectTransform rect = transform as RectTransform;
            if (rect == null)
                return;
            float clamped = Mathf.Clamp01(scale);
            float inset = (1f - clamped) * 0.5f;
            rect.anchorMin = new Vector2(inset, inset);
            rect.anchorMax = new Vector2(1f - inset, 1f - inset);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
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
            bool heroRunner = GetComponent<AssassinRunner>() != null;
            if (!heroRunner && animator != null && animator.runtimeAnimatorController != null && !string.IsNullOrEmpty(state))
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
