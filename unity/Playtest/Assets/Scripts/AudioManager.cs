using System.Collections.Generic;
using UnityEngine;

namespace Playtest
{
    public sealed class AudioManager : ScriptableObject
    {
        const string LibraryResource = "AudioLibrary";

        public AudioClip[] musicClips = new AudioClip[0];
        public AudioClip[] sfxClips = new AudioClip[0];

        PresentationConfig presentation;
        AudioLibrary library;
        AudioSource music;
        AudioSource effects;
        string currentTrack;

        public void Open(PresentationConfig source, GameObject host)
        {
            presentation = source;
            library = Resources.Load<AudioLibrary>(LibraryResource);
            if (musicClips == null)
                musicClips = new AudioClip[0];
            if (sfxClips == null)
                sfxClips = new AudioClip[0];
            AdoptRealClips();
            if (host != null)
            {
                music = host.AddComponent<AudioSource>();
                music.playOnAwake = false;
                music.loop = false;
                music.mute = true;
                music.clip = null;
                effects = host.AddComponent<AudioSource>();
                effects.playOnAwake = false;
                effects.loop = false;
                effects.mute = true;
                effects.clip = null;
            }
            if (HasClips(musicClips) && presentation != null)
            {
                if (music != null)
                    music.mute = false;
                PlayTrack(presentation.IdleTrack);
            }
        }

        public void SetBoss(bool boss)
        {
            if (!HasClips(musicClips) || presentation == null)
                return;
            PlayTrack(boss ? presentation.BossTrack : presentation.IdleTrack);
        }

        public void PlayClick()
        {
            if (!HasClips(sfxClips) || presentation == null)
                return;
            PlayEffect(presentation.ClickSfx);
        }

        public void PlayUpgrade()
        {
            if (!HasClips(sfxClips) || presentation == null)
                return;
            PlayEffect(presentation.UpgradeSfx);
        }

        public void PlayCrit()
        {
            if (!HasClips(sfxClips) || presentation == null)
                return;
            PlayEffect(presentation.CritSfx);
        }

        void AdoptRealClips()
        {
            if (presentation == null)
                return;
            if (!HasClips(musicClips))
                musicClips = Collect(new[] { presentation.IdleTrack, presentation.BossTrack });
            if (!HasClips(sfxClips))
                sfxClips = Collect(new[] { presentation.ClickSfx, presentation.UpgradeSfx, presentation.CritSfx });
        }

        AudioClip[] Collect(string[] ids)
        {
            List<AudioClip> found = new List<AudioClip>();
            if (ids == null)
                return found.ToArray();
            for (int index = 0; index < ids.Length; index++)
            {
                AudioClip clip = FindReal(ids[index]);
                if (clip != null)
                    found.Add(clip);
            }
            return found.ToArray();
        }

        void PlayTrack(string id)
        {
            if (!HasClips(musicClips) || music == null || string.IsNullOrEmpty(id) || id == currentTrack)
                return;
            AudioClip clip = Resolve(id, musicClips);
            if (clip == null)
                return;
            currentTrack = id;
            music.mute = false;
            music.clip = clip;
            music.loop = true;
            music.Play();
        }

        void PlayEffect(string id)
        {
            if (!HasClips(sfxClips) || effects == null || string.IsNullOrEmpty(id))
                return;
            AudioClip clip = Resolve(id, sfxClips);
            if (clip == null)
                return;
            effects.mute = false;
            effects.PlayOneShot(clip);
        }

        AudioClip Resolve(string id, AudioClip[] assigned)
        {
            AudioClip real = FindReal(id);
            if (real != null)
                return real;
            return Match(id, assigned);
        }

        AudioClip FindReal(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
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
            return null;
        }

        static AudioClip Match(string id, AudioClip[] assigned)
        {
            if (assigned == null || string.IsNullOrEmpty(id))
                return null;
            string leaf = id;
            int slash = id.LastIndexOf('/');
            if (slash >= 0 && slash + 1 < id.Length)
                leaf = id.Substring(slash + 1);
            AudioClip fallback = null;
            int available = 0;
            for (int index = 0; index < assigned.Length; index++)
            {
                AudioClip clip = assigned[index];
                if (clip == null)
                    continue;
                available++;
                if (fallback == null)
                    fallback = clip;
                if (clip.name == id || clip.name == leaf)
                    return clip;
            }
            return available == 1 ? fallback : null;
        }

        static bool HasClips(AudioClip[] clips)
        {
            return clips != null && clips.Length > 0;
        }
    }
}
