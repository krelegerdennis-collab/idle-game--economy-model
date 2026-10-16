using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Playtest
{
    public sealed class EnemyController : MonoBehaviour
    {
        readonly List<CharacterController> actors = new List<CharacterController>();

        public void Bind(PresentationConfig presentation, RuntimeAnimatorController controller, Dictionary<string, byte[]> pictures, RectTransform stage)
        {
            if (presentation == null || stage == null)
                return;
            transform.SetParent(stage, false);
            RectTransform row = transform as RectTransform;
            if (row != null)
            {
                row.anchorMin = new Vector2(0f, presentation.ActorSplit);
                row.anchorMax = Vector2.one;
                row.offsetMin = Vector2.zero;
                row.offsetMax = Vector2.zero;
                row.pivot = new Vector2(0.5f, 0.5f);
            }
            HorizontalLayoutGroup group = gameObject.GetComponent<HorizontalLayoutGroup>();
            if (group == null)
                group = gameObject.AddComponent<HorizontalLayoutGroup>();
            group.padding = new RectOffset(
                presentation.Padding,
                presentation.Padding,
                presentation.Padding,
                presentation.Padding);
            group.spacing = presentation.Spacing;
            group.childAlignment = TextAnchor.MiddleCenter;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = true;
            if (presentation.Enemies == null)
                return;
            for (int index = 0; index < presentation.Enemies.Length; index++)
            {
                ActorVisual visual = presentation.Enemies[index];
                GameObject actor = new GameObject(visual.Id, typeof(RectTransform), typeof(LayoutElement));
                actor.transform.SetParent(transform, false);
                LayoutElement element = actor.GetComponent<LayoutElement>();
                element.flexibleWidth = 1f;
                element.flexibleHeight = 1f;
                CharacterController character = actor.AddComponent<CharacterController>();
                byte[] png = null;
                if (pictures != null && !string.IsNullOrEmpty(visual.SpritePath))
                    pictures.TryGetValue(visual.SpritePath, out png);
                character.Bind(presentation, visual, controller, png);
                actors.Add(character);
            }
        }

        public void Cue(string state)
        {
            for (int index = 0; index < actors.Count; index++)
                actors[index].Cue(state);
        }
    }
}
