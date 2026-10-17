using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Playtest
{
    public sealed class AudioClipPostprocessor : AssetPostprocessor
    {
        const string LibraryPath = "Assets/Resources/AudioLibrary.asset";
        const string PresentationPath = "Assets/StreamingAssets/presentation.json";

        static void OnPostprocessAllAssets(
            string[] imported,
            string[] deleted,
            string[] moved,
            string[] movedFrom)
        {
            HashSet<string> folders = AudioFolders();
            if (folders.Count == 0 || !Touches(imported, deleted, moved, movedFrom, folders))
                return;
            AudioLibrary library = AssetDatabase.LoadAssetAtPath<AudioLibrary>(LibraryPath);
            if (library == null)
                return;
            if (library.Slots == null)
                library.Slots = new List<AudioSlot>();
            library.Slots.Clear();
            foreach (string folder in folders)
            {
                if (!AssetDatabase.IsValidFolder(folder))
                    continue;
                string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { folder });
                for (int index = 0; index < guids.Length; index++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[index]);
                    AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                    if (clip == null || !path.StartsWith("Assets/"))
                        continue;
                    string id = path.Substring("Assets/".Length);
                    int dot = id.LastIndexOf('.');
                    if (dot > 0)
                        id = id.Substring(0, dot);
                    library.Slots.Add(new AudioSlot { Id = id, Clip = clip });
                }
            }
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
        }

        static bool Touches(string[] imported, string[] deleted, string[] moved, string[] movedFrom, HashSet<string> folders)
        {
            return Hits(imported, folders) || Hits(deleted, folders) || Hits(moved, folders) || Hits(movedFrom, folders);
        }

        static bool Hits(string[] paths, HashSet<string> folders)
        {
            if (paths == null)
                return false;
            for (int index = 0; index < paths.Length; index++)
            {
                foreach (string folder in folders)
                {
                    if (paths[index].StartsWith(folder + "/"))
                        return true;
                }
            }
            return false;
        }

        static HashSet<string> AudioFolders()
        {
            HashSet<string> folders = new HashSet<string>();
            if (!File.Exists(PresentationPath))
                return folders;
            JsonValue root;
            try
            {
                root = JsonMini.Parse(File.ReadAllText(PresentationPath));
            }
            catch (System.Exception)
            {
                return folders;
            }
            if (root.Type != JsonValue.Kind.Object || !root.Map.ContainsKey("audio"))
                return folders;
            JsonValue audio = root.Map["audio"];
            string[] keys = { "idleTrack", "bossTrack", "clickSfx", "upgradeSfx", "critSfx" };
            for (int index = 0; index < keys.Length; index++)
            {
                if (!audio.Map.ContainsKey(keys[index]) || audio.Map[keys[index]].Type != JsonValue.Kind.String)
                    continue;
                string id = audio.Map[keys[index]].Text;
                int slash = id.LastIndexOf('/');
                if (slash <= 0)
                    continue;
                folders.Add("Assets/" + id.Substring(0, slash));
            }
            return folders;
        }
    }
}
