using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Playtest
{
    public sealed class PresentationFeedback : MonoBehaviour
    {
        PresentationConfig presentation;
        RectTransform root;
        Camera view;
        Vector3 cameraRest;
        float shakeUntil;
        ParticleSystem confetti;
        TMP_FontAsset fontAsset;
        Font builtinFont;

        public void Bind(PresentationConfig source, RectTransform shakeRoot, Camera camera, TMP_FontAsset tmpFont, Font font)
        {
            presentation = source;
            root = shakeRoot;
            view = camera;
            builtinFont = font;
            fontAsset = tmpFont;
            if (view != null)
                cameraRest = view.transform.localPosition;
            confetti = CreateConfetti();
        }

        public void Float(string text, Color color, RectTransform near)
        {
            if (presentation == null || root == null)
                return;
            GameObject item = new GameObject("Float", typeof(RectTransform));
            item.transform.SetParent(root, false);
            RectTransform rect = item.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(presentation.ReferenceWidth * 0.5f, presentation.BodyMinHeight);
            Vector2 local;
            Vector2 screen = near == null
                ? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)
                : RectTransformUtility.WorldToScreenPoint(null, near.position);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out local);
            rect.anchoredPosition = local;
            Graphic label = CreateLabel(item.transform, text, color);
            FloatingLabel motion = item.AddComponent<FloatingLabel>();
            motion.Rect = rect;
            motion.Graphic = label;
            motion.Born = Time.unscaledTime;
            motion.Life = presentation.FloatSeconds;
            motion.Rise = presentation.FloatRise;
            motion.Start = local;
        }

        public void Shake()
        {
            if (presentation == null)
                return;
            shakeUntil = Time.unscaledTime + presentation.ShakeSeconds;
        }

        public void Confetti()
        {
            if (confetti == null)
                return;
            confetti.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            confetti.Play();
        }

        void Update()
        {
            if (presentation == null)
                return;
            float remaining = shakeUntil - Time.unscaledTime;
            if (remaining <= 0f)
            {
                if (view != null)
                    view.transform.localPosition = cameraRest;
                if (root != null)
                    root.anchoredPosition = Vector2.zero;
                return;
            }
            float amplitude = presentation.ShakePixels * (remaining / presentation.ShakeSeconds);
            float x = Mathf.Sin(Time.unscaledTime * presentation.ShakeFrequency) * amplitude;
            float y = Mathf.Cos(Time.unscaledTime * presentation.ShakeFrequency) * amplitude;
            if (view != null)
                view.transform.localPosition = cameraRest + new Vector3(x, y, 0f) * presentation.CameraUnitsPerPixel;
            if (root != null)
                root.anchoredPosition = new Vector2(x, y);
        }

        Graphic CreateLabel(Transform parent, string text, Color color)
        {
            if (fontAsset != null)
            {
                TextMeshProUGUI label = parent.gameObject.AddComponent<TextMeshProUGUI>();
                label.font = fontAsset;
                label.fontSize = presentation.GeneratorSize;
                label.alignment = TextAlignmentOptions.Center;
                label.color = color;
                label.text = text;
                label.raycastTarget = false;
                return label;
            }
            Text labelText = parent.gameObject.AddComponent<Text>();
            labelText.font = builtinFont;
            labelText.fontSize = presentation.GeneratorSize;
            labelText.alignment = TextAnchor.MiddleCenter;
            labelText.color = color;
            labelText.text = text;
            labelText.raycastTarget = false;
            return labelText;
        }

        ParticleSystem CreateConfetti()
        {
            GameObject host = new GameObject("Confetti");
            if (view != null)
                host.transform.SetParent(view.transform, false);
            host.transform.localPosition = new Vector3(0f, 0f, 5f);
            ParticleSystem system = host.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = system.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = presentation.ConfettiSeconds;
            main.startLifetime = presentation.ConfettiSeconds;
            main.startSpeed = presentation.ConfettiSpeed;
            main.startSize = presentation.ConfettiSize;
            main.startColor = presentation.Currency;
            main.maxParticles = presentation.ConfettiCount;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0f;
            short burst = (short)Mathf.Clamp(presentation.ConfettiCount, 1, short.MaxValue);
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, burst) });
            ParticleSystem.ShapeModule shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.radius = presentation.ConfettiRadius;
            ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
                renderer.material = new Material(shader);
            return system;
        }

        sealed class FloatingLabel : MonoBehaviour
        {
            public RectTransform Rect;
            public Graphic Graphic;
            public float Born;
            public float Life;
            public float Rise;
            public Vector2 Start;

            void Update()
            {
                float age = Life <= 0f ? 1f : (Time.unscaledTime - Born) / Life;
                if (age >= 1f)
                {
                    Destroy(gameObject);
                    return;
                }
                Rect.anchoredPosition = Start + new Vector2(0f, Rise * age);
                if (Graphic == null)
                    return;
                Color color = Graphic.color;
                color.a = 1f - age;
                Graphic.color = color;
            }
        }
    }
}
