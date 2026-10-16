using System.Collections.Generic;
using UnityEngine;

namespace Playtest
{
    public sealed class EnemyController : MonoBehaviour
    {
        readonly List<CharacterController> actors = new List<CharacterController>();

        public void Bind(PresentationConfig presentation, RuntimeAnimatorController controller, Dictionary<string, byte[]> pictures, RectTransform dock)
        {
            if (presentation == null || dock == null)
                return;
            transform.SetParent(dock, false);
            RectTransform row = transform as RectTransform;
            if (row != null)
            {
                row.anchorMin = Vector2.zero;
                row.anchorMax = Vector2.one;
                row.offsetMin = Vector2.zero;
                row.offsetMax = Vector2.zero;
                row.pivot = new Vector2(0.5f, 0.5f);
            }
            if (presentation.Enemies == null)
                return;
            int count = presentation.Enemies.Length;
            float total = 0f;
            for (int index = 0; index < count; index++)
                total += presentation.Enemies[index].Width;
            if (count > 1)
                total += presentation.Spacing * (count - 1);
            float cursor = -total * 0.5f;
            float baseline = presentation.Padding;
            if (presentation.Hero != null)
                baseline += presentation.Hero.Height;
            if (count > 0)
                baseline += presentation.Spacing;
            for (int index = 0; index < count; index++)
            {
                ActorVisual visual = presentation.Enemies[index];
                GameObject actor = new GameObject(visual.Id, typeof(RectTransform));
                actor.transform.SetParent(transform, false);
                CharacterController character = actor.AddComponent<CharacterController>();
                byte[] png = null;
                if (pictures != null && !string.IsNullOrEmpty(visual.SpritePath))
                    pictures.TryGetValue(visual.SpritePath, out png);
                character.Bind(presentation, visual, controller, png);
                float x = cursor + visual.Width * 0.5f;
                character.Pin(new Vector2(0.5f, 0f), new Vector2(x, baseline));
                cursor += visual.Width + presentation.Spacing;
                actors.Add(character);
            }
        }

        public float RowHeight()
        {
            float height = 0f;
            for (int index = 0; index < actors.Count; index++)
            {
                if (actors[index].Height > height)
                    height = actors[index].Height;
            }
            return height;
        }

        public void Cue(string state)
        {
            for (int index = 0; index < actors.Count; index++)
                actors[index].Cue(state);
        }
    }
}
