using System.Runtime.InteropServices;

namespace Playtest
{
    /// <summary>
    /// Browser download for the JSONL log. Unity 2022 has no Application.ExternalEval.
    /// The jslib runs only in a WebGL player. A browser may ignore a download that
    /// was not started by a click, so the on-screen button is the reliable path.
    /// </summary>
    public static class WebGLDownload
    {
        public static void Save(string filename, string content)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            PlaytestDownload(filename, content ?? "");
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern void PlaytestDownload(string filename, string content);
#endif
    }
}
