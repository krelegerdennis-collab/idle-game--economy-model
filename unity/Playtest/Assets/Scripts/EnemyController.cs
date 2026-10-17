using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Playtest
{
    public sealed class EnemyController : MonoBehaviour
    {
        readonly List<CharacterController> actors = new List<CharacterController>();
        readonly List<ActorVisual> visuals = new List<ActorVisual>();
        PresentationConfig presentation;

        public void Bind(PresentationConfig source, RuntimeAnimatorController controller, Dictionary<string, byte[]> pictures, RectTransform stage)
        {
            presentation = source;
            if (presentation == null || stage == null)
                return;
            transform.SetParent(stage, false);
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
            for (int index = 0; index < presentation.Enemies.Length; index++)
            {
                ActorVisual visual = presentation.Enemies[index];
                GameObject actor = new GameObject(visual.Id, typeof(RectTransform));
                actor.transform.SetParent(transform, false);
                CharacterController character = actor.AddComponent<CharacterController>();
                byte[] png = null;
                if (pictures != null && !string.IsNullOrEmpty(visual.SpritePath))
                    pictures.TryGetValue(visual.SpritePath, out png);
                character.Bind(presentation, visual, controller, png);
                actors.Add(character);
                visuals.Add(visual);
            }
        }

        public void Layout(RectTransform stage, float reservedRight)
        {
            if (presentation == null || stage == null || visuals.Count == 0)
                return;
            float stageWidth = stage.rect.width;
            float stageHeight = stage.rect.height;
            if (stageWidth < 1f || stageHeight < 1f)
                return;
            float left = presentation.Padding;
            float areaWidth = stageWidth - reservedRight - left;
            if (areaWidth < 1f)
                areaWidth = stageWidth * presentation.ActorSplit;
            if (areaWidth < 1f)
                areaWidth = 1f;
            float areaHeight = stageHeight - presentation.Padding * 2f;
            if (areaHeight < 1f)
                areaHeight = stageHeight;
            bool stack = areaHeight >= areaWidth;
            float along = 0f;
            float cross = 0f;
            for (int index = 0; index < visuals.Count; index++)
            {
                float width = Mathf.Max(1, visuals[index].Width);
                float height = Mathf.Max(1, visuals[index].Height);
                along += stack ? height : width;
                float other = stack ? width : height;
                if (other > cross)
                    cross = other;
            }
            along += presentation.Spacing * (visuals.Count - 1);
            if (along < 1f)
                along = 1f;
            if (cross < 1f)
                cross = 1f;
            float room = stack ? areaHeight : areaWidth;
            float scale = room / along;
            float crossRoom = stack ? areaWidth : stageHeight * presentation.HeroScale;
            if (crossRoom < 1f)
                crossRoom = stack ? areaWidth : areaHeight;
            if (cross * scale > crossRoom)
                scale = crossRoom / cross;
            float used = along * scale;
            if (stack)
            {
                float cursor = used * 0.5f;
                for (int index = 0; index < visuals.Count; index++)
                {
                    float width = Mathf.Max(1, visuals[index].Width) * scale;
                    float height = Mathf.Max(1, visuals[index].Height) * scale;
                    RectTransform rect = actors[index].transform as RectTransform;
                    if (rect == null)
                        continue;
                    rect.anchorMin = new Vector2(0f, 0.5f);
                    rect.anchorMax = new Vector2(0f, 0.5f);
                    rect.pivot = new Vector2(0f, 1f);
                    rect.sizeDelta = new Vector2(width, height);
                    rect.anchoredPosition = new Vector2(left + (areaWidth - width) * 0.5f, cursor);
                    cursor -= height + presentation.Spacing;
                }
                return;
            }
            float x = left + (areaWidth - used) * 0.5f;
            if (x < left)
                x = left;
            for (int index = 0; index < visuals.Count; index++)
            {
                float width = Mathf.Max(1, visuals[index].Width) * scale;
                float height = Mathf.Max(1, visuals[index].Height) * scale;
                RectTransform rect = actors[index].transform as RectTransform;
                if (rect == null)
                    continue;
                rect.anchorMin = new Vector2(0f, 0.5f);
                rect.anchorMax = new Vector2(0f, 0.5f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.sizeDelta = new Vector2(width, height);
                rect.anchoredPosition = new Vector2(x, 0f);
                x += width + presentation.Spacing;
            }
        }

        public void Cue(string state)
        {
            for (int index = 0; index < actors.Count; index++)
                actors[index].Cue(state);
        }
    }
}
