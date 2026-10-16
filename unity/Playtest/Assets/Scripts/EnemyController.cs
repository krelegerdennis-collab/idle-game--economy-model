using System.Collections.Generic;
using UnityEngine;

namespace Playtest
{
    public sealed class EnemyController : MonoBehaviour
    {
        readonly List<CharacterController> actors = new List<CharacterController>();

        public void Bind(PresentationConfig presentation, RuntimeAnimatorController controller)
        {
            if (presentation.Enemies == null)
                return;
            for (int index = 0; index < presentation.Enemies.Length; index++)
            {
                ActorVisual visual = presentation.Enemies[index];
                GameObject actor = new GameObject(visual.Id);
                actor.transform.SetParent(transform, false);
                CharacterController character = actor.AddComponent<CharacterController>();
                character.Bind(presentation, visual, controller);
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
