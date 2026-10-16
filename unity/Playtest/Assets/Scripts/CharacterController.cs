using UnityEngine;

namespace Playtest
{
    public sealed class CharacterController : MonoBehaviour
    {
        SpriteRenderer sprite;
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
            Sprite picture = FromPng(png, visual, source.PixelsPerUnit);
            baseColor = picture == null ? visual.Color : Color.white;
            sprite = gameObject.AddComponent<SpriteRenderer>();
            sprite.sprite = picture == null ? Rectangle(visual, source.PixelsPerUnit) : picture;
            sprite.color = baseColor;
            animator = gameObject.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            transform.position = new Vector3(visual.X, visual.Y, 0f);
            Show(idleState);
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
            if (sprite == null)
                return;
            bool hurt = presentation != null && state == presentation.PurchaseEnemy;
            sprite.color = hurt ? presentation.Critical : baseColor;
        }

        static Sprite FromPng(byte[] png, ActorVisual visual, float pixelsPerUnit)
        {
            if (png == null || png.Length == 0 || visual.Height < 1 || pixelsPerUnit <= 0f)
                return null;
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(png))
                return null;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            float worldHeight = visual.Height / pixelsPerUnit;
            float units = texture.height / worldHeight;
            return Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                units);
        }

        static Sprite Rectangle(ActorVisual visual, float pixelsPerUnit)
        {
            Texture2D texture = new Texture2D(visual.Width, visual.Height, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[visual.Width * visual.Height];
            for (int index = 0; index < pixels.Length; index++)
                pixels[index] = Color.white;
            texture.SetPixels(pixels);
            texture.filterMode = FilterMode.Point;
            texture.Apply();
            return Sprite.Create(
                texture,
                new Rect(0f, 0f, visual.Width, visual.Height),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit);
        }
    }
}
