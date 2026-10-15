using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Playtest
{
    /// <summary>
    /// GitHub Pages does not set Content-Encoding for Brotli or Gzip.
    /// The smoke build therefore ships uncompressed WebGL.
    /// </summary>
    public sealed class WebGLPagesSettings : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.WebGL)
                return;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.threadsSupport = false;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.FullWithStacktrace;
            PlayerSettings.WebGL.template = "PROJECT:Smoke";
        }
    }
}
