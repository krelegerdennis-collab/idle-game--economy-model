using System;
using System.Collections.Generic;
using UnityEngine;

namespace Playtest
{
    [Serializable]
    public sealed class AudioSlot
    {
        public string Id;
        public AudioClip Clip;
    }

    public sealed class AudioLibrary : ScriptableObject
    {
        public List<AudioSlot> Slots = new List<AudioSlot>();

        public AudioClip Find(string id)
        {
            if (Slots == null || string.IsNullOrEmpty(id))
                return null;
            for (int index = 0; index < Slots.Count; index++)
            {
                AudioSlot slot = Slots[index];
                if (slot != null && slot.Id == id)
                    return slot.Clip;
            }
            return null;
        }
    }
}
