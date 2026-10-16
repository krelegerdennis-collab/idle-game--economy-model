using System.Collections.Generic;
using UnityEngine;

namespace Playtest
{
    public sealed class AudioManager : ScriptableObject
    {
        const string LibraryResource = "AudioLibrary";

        PresentationConfig presentation;
        AudioLibrary library;
        AudioSource music;
        AudioSource effects;
        string currentTrack;
        readonly Dictionary<string, AudioClip> generated = new Dictionary<string, AudioClip>();

        public void Open(PresentationConfig source, GameObject host)
        {
            presentation = source;
            library = Resources.Load<AudioLibrary>(LibraryResource);
            music = host.AddComponent<AudioSource>();
            music.playOnAwake = false;
            music.loop = true;
            effects = host.AddComponent<AudioSource>();
            effects.playOnAwake = false;
            effects.loop = false;
            PlayTrack(presentation.IdleTrack);
        }

        public void SetBoss(bool boss)
        {
            if (presentation == null)
                return;
            PlayTrack(boss ? presentation.BossTrack : presentation.IdleTrack);
        }

        public void PlayClick()
        {
            PlayEffect(presentation == null ? null : presentation.ClickSfx);
        }

        public void PlayUpgrade()
        {
            PlayEffect(presentation == null ? null : presentation.UpgradeSfx);
        }

        public void PlayCrit()
        {
            PlayEffect(presentation == null ? null : presentation.CritSfx);
        }

        void PlayTrack(string id)
        {
            if (music == null || string.IsNullOrEmpty(id) || id == currentTrack)
                return;
            currentTrack = id;
            AudioClip clip = Resolve(id);
            if (clip == null)
                return;
            music.clip = clip;
            music.loop = true;
            music.Play();
        }

        void PlayEffect(string id)
        {
            if (effects == null || string.IsNullOrEmpty(id))
                return;
            AudioClip clip = Resolve(id);
            if (clip == null)
                return;
            effects.PlayOneShot(clip);
        }

        AudioClip Resolve(string id)
        {
            if (library != null)
            {
                AudioClip assigned = library.Find(id);
                if (assigned != null)
                    return assigned;
            }
#if UNITY_EDITOR
            if (presentation != null && presentation.Extensions != null)
            {
                for (int index = 0; index < presentation.Extensions.Length; index++)
                {
                    string extension = presentation.Extensions[index];
                    AudioClip imported = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/" + id + extension);
                    if (imported != null)
                        return imported;
                }
            }
#endif
            return Placeholder(id);
        }

        AudioClip Placeholder(string id)
        {
            AudioClip cached;
            if (generated.TryGetValue(id, out cached) && cached != null)
                return cached;
            if (presentation == null || presentation.SampleRate < 1)
                return null;
            float seconds = presentation.Loops(id) ? presentation.LoopSeconds : presentation.SfxSeconds;
            int length = Mathf.CeilToInt(seconds * presentation.SampleRate);
            if (length < 1)
                return null;
            float[] samples = new float[length];
            float hz = presentation.ToneHz(id);
            float amplitude = presentation.Amplitude;
            for (int index = 0; index < length; index++)
            {
                float envelope = 1f;
                if (!presentation.Loops(id))
                    envelope = 1f - (index / (float)length);
                samples[index] = Mathf.Sin(2f * Mathf.PI * hz * index / presentation.SampleRate) * amplitude * envelope;
            }
            AudioClip clip = AudioClip.Create(id, length, 1, presentation.SampleRate, false);
            clip.SetData(samples, 0);
            generated[id] = clip;
            return clip;
        }
    }
}
