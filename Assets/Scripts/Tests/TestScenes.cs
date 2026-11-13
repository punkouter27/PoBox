using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PoBox.Tests
{
    /// <summary>
    /// What every fixture's tear-down does: leaves an empty scene behind. Each test loads the scene it needs and used to
    /// leave it loaded, so the next fixture run (the MuJoCo plugin's own play-mode tests, which take
    /// <c>MjScene.Instance</c> and build bodies beside whatever is there) started inside a ring full of boxers and failed.
    /// Also puts back what a test that stopped half-way may have left set: the clock's speed and the pretend notch.
    /// </summary>
    public static class TestScenes
    {
        const string Empty = "PoBox Tests Empty";

        public static IEnumerator Leave()
        {
            Time.timeScale = 1f;
            PoBox.UI.HudView.SafeAreaOverride = null;

            Scene empty = SceneManager.GetSceneByName(Empty);
            if (!empty.IsValid()) empty = SceneManager.CreateScene(Empty);
            SceneManager.SetActiveScene(empty);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (s == empty || !s.isLoaded) continue;
                // Unity's PlaymodeTestsController lives in InitTestScene until a Single scene load moves it;
                // unloading InitTestScene directly destroys the runner and hangs the rest of the suite.
                if (s.name.StartsWith("InitTestScene"))
                {
                    foreach (GameObject g in s.GetRootGameObjects())
                        if ((g.hideFlags & HideFlags.DontSave) == 0) Object.Destroy(g);
                    continue;
                }
                AsyncOperation op = SceneManager.UnloadSceneAsync(s);
                if (op != null) yield return op;
            }
            // A frame for the destroyed objects' OnDestroy (the plugin frees its model there).
            yield return null;
        }
    }
}
