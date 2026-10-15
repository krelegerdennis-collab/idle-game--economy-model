using System.IO;
using UnityEngine;

namespace Playtest
{
    /// <summary>
    /// Copies the local JSONL log when the app pauses or quits.
    /// Android: /sdcard/Android/data/&lt;package&gt;/files/playtest_log.jsonl
    /// iOS: Application.persistentDataPath. Other players use that same folder.
    /// </summary>
    public sealed class ExportLogs : MonoBehaviour
    {
        public const string FileName = "playtest_log.jsonl";

        AnalyticsBridge analytics;

        public void Bind(AnalyticsBridge bridge)
        {
            analytics = bridge;
        }

        public static string DestinationPath()
        {
            if (Application.platform == RuntimePlatform.Android)
                return "/sdcard/Android/data/" + Application.identifier + "/files/" + FileName;
            return Path.Combine(Application.persistentDataPath, FileName);
        }

        public void ExportNow()
        {
            if (analytics == null || string.IsNullOrEmpty(analytics.LogPath))
                return;
            analytics.Flush();
            string source = analytics.LogPath;
            if (!File.Exists(source))
                return;
            string destination = DestinationPath();
            string sourceFull = Path.GetFullPath(source);
            string destinationFull = Path.GetFullPath(destination);
            if (sourceFull == destinationFull)
                return;
            string directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.Copy(source, destination, true);
        }

        void OnApplicationPause(bool paused)
        {
            if (paused)
                ExportNow();
        }

        void OnApplicationQuit()
        {
            ExportNow();
        }
    }
}
