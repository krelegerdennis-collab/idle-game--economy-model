using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Playtest
{
    public sealed class AssassinRunner : MonoBehaviour
    {
        const string ControllerResource = "Animations/AssassinAnimator";
        const string ControllerAsset = "Assets/Animations/AssassinAnimator.controller";

        Image image;
        Animator animator;
        AssassinConfig config;
        PresentationConfig presentation;
        readonly Dictionary<string, Sprite[]> clips = new Dictionary<string, Sprite[]>();
        AssassinClip active;
        Sprite applied;
        int frame;
        float elapsed;
        bool running = true;
        bool fallbackDrives = true;

        public void Bind(AssassinConfig source, PresentationConfig theme, Dictionary<string, byte[]> pictures)
        {
            config = source;
            presentation = theme;
            image = GetComponent<Image>();
            if (image == null)
                image = gameObject.AddComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
            animator = GetComponent<Animator>();
            if (animator == null)
                animator = gameObject.AddComponent<Animator>();
            animator.updateMode = AnimatorUpdateMode.UnscaledTime;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            BuildClips(pictures);
            RuntimeAnimatorController controller = LoadController();
            animator.runtimeAnimatorController = controller;
            running = true;
            if (controller != null && config != null)
            {
                animator.SetBool(config.RunningParameter, true);
                if (!string.IsNullOrEmpty(config.DefaultState))
                    animator.Play(config.DefaultState, 0, 0f);
            }
            Begin(config == null ? null : config.Run);
        }

        public void Attack()
        {
            if (config == null || config.Attack == null)
                return;
            if (animator != null && animator.runtimeAnimatorController != null && !string.IsNullOrEmpty(config.AttackTrigger))
                animator.SetTrigger(config.AttackTrigger);
            Begin(config.Attack);
            if (fallbackDrives)
                ApplySprite();
        }

        void Update()
        {
            if (!fallbackDrives || active == null || active.Fps <= 0f)
                return;
            Sprite[] frames = Frames(active);
            if (frames == null || frames.Length == 0)
                return;
            float step = 1f / active.Fps;
            elapsed += Time.unscaledDeltaTime;
            while (elapsed >= step)
            {
                elapsed -= step;
                frame += 1;
                if (frame < frames.Length)
                    continue;
                if (active.Loop)
                {
                    frame = 0;
                    continue;
                }
                Begin(running ? config.Run : config.Idle);
                return;
            }
        }

        void LateUpdate()
        {
            bool animatorOwns = AnimatorDriving()
                && image != null
                && image.sprite != null
                && image.sprite != applied;
            fallbackDrives = !animatorOwns;
            if (fallbackDrives)
                ApplySprite();
        }

        void Begin(AssassinClip clip)
        {
            active = clip;
            frame = 0;
            elapsed = 0f;
        }

        void ApplySprite()
        {
            if (image == null || active == null)
                return;
            Sprite[] frames = Frames(active);
            if (frames == null || frames.Length == 0)
                return;
            int index = frame;
            if (index < 0 || index >= frames.Length)
                index = 0;
            Sprite picture = frames[index];
            if (picture == null)
                return;
            image.sprite = picture;
            applied = picture;
        }

        bool AnimatorDriving()
        {
            if (animator == null || animator.runtimeAnimatorController == null || config == null)
                return false;
            if (!animator.isInitialized)
                return false;
            AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
            if (info.length <= 0f)
                return false;
            return Matches(info, config.Run) || Matches(info, config.Attack) || Matches(info, config.Idle);
        }

        static bool Matches(AnimatorStateInfo info, AssassinClip clip)
        {
            return clip != null && !string.IsNullOrEmpty(clip.Name) && info.IsName(clip.Name);
        }

        Sprite[] Frames(AssassinClip clip)
        {
            Sprite[] frames;
            if (clip == null || string.IsNullOrEmpty(clip.Name) || !clips.TryGetValue(clip.Name, out frames))
                return null;
            return frames;
        }

        void BuildClips(Dictionary<string, byte[]> pictures)
        {
            clips.Clear();
            if (config == null)
                return;
            Store(config.Run, pictures);
            Store(config.Attack, pictures);
            Store(config.Idle, pictures);
        }

        void Store(AssassinClip clip, Dictionary<string, byte[]> pictures)
        {
            if (clip == null || string.IsNullOrEmpty(clip.Name) || clip.Frames == null)
                return;
            List<Sprite> frames = new List<Sprite>();
            for (int index = 0; index < clip.Frames.Length; index++)
            {
                byte[] png = null;
                string path = clip.Frames[index];
                if (pictures != null && !string.IsNullOrEmpty(path))
                    pictures.TryGetValue(path, out png);
                Sprite picture = FromPng(png);
                if (picture != null)
                    frames.Add(picture);
            }
            if (frames.Count > 0)
                clips[clip.Name] = frames.ToArray();
        }

        Sprite FromPng(byte[] png)
        {
            if (png == null || png.Length == 0)
                return null;
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(png))
                return null;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            float pixelsPerUnit = presentation == null || presentation.PixelsPerUnit <= 0f
                ? 1f
                : presentation.PixelsPerUnit;
            return Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit);
        }

        static RuntimeAnimatorController LoadController()
        {
            RuntimeAnimatorController loaded = Resources.Load<RuntimeAnimatorController>(ControllerResource);
            if (loaded != null)
                return loaded;
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerAsset);
#else
            return null;
#endif
        }
    }
}
