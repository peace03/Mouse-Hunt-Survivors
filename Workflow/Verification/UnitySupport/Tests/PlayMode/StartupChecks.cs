using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MidnightMouse.Workflow.Tests
{
    public sealed class StartupChecks
    {
        [UnityTest]
        public IEnumerator FirstSceneLoadsAndAdvancesFrames()
        {
#if UNITY_EDITOR
            var path = EditorBuildSettings.scenes.First(scene => scene.enabled).path;
#else
            var path = SceneUtility.GetScenePathByBuildIndex(0);
#endif
            yield return SceneManager.LoadSceneAsync(path);
            var frame = Time.frameCount;
            yield return null;
            yield return null;
            Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(path));
            Assert.That(SceneManager.GetActiveScene().isLoaded, Is.True);
            Assert.That(Time.frameCount, Is.GreaterThan(frame));
            Assert.That(Object.FindObjectsByType<Camera>().Any(camera => camera.isActiveAndEnabled),
                Is.True, "첫 씬에 활성 카메라가 필요합니다.");
            LogAssert.NoUnexpectedReceived();
        }
    }
}
