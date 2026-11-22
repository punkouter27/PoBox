using System;
using System.Globalization;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace PoBox.EditorTools
{
    /// <summary>
    /// Stamps PlayerSettings.bundleVersion with the build's one version, UTC yyMMdd, before a WebGL build: the page
    /// template prints v{PRODUCT_VERSION} in its footer and Application.version carries it. scripts/publish_webgl.ps1
    /// computes the stamp once and passes it in PO_BUILD_STAMP, so the player, index.html and diag.html agree even
    /// across midnight; a build from the menu stamps today (UTC). Android builds keep the version AndroidPrep gives them.
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
            string given = Environment.GetEnvironmentVariable("PO_BUILD_STAMP");
            PlayerSettings.bundleVersion = !string.IsNullOrEmpty(given) && given.Length == 6 && int.TryParse(given, out _)
                ? given
                : DateTime.UtcNow.ToString("yyMMdd", CultureInfo.InvariantCulture);
        }
    }
}
