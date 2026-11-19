using System;
using System.Globalization;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace PoBox.EditorTools
{
    /// <summary>
    /// Stamps PlayerSettings.bundleVersion with YYMMDD (UTC) before a WebGL build, because the Po
    /// WebGL page template prints v{PRODUCT_VERSION} in the lower-right corner. Android builds keep
    /// the version AndroidPrep gives them.
    /// </summary>
    public sealed class WebGLBuildStamp : IPreprocessBuildWithReport
    {
        public int callbackOrder => -100;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.WebGL)
            {
                return;
            }
            PlayerSettings.bundleVersion = DateTime.UtcNow.ToString("yyMMdd", CultureInfo.InvariantCulture);
        }
    }
}
