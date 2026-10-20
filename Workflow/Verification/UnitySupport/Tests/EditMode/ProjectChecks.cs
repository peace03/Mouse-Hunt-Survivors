using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MidnightMouse.Workflow.Tests
{
    public sealed class ProjectChecks
    {
        [Test]
        public void EnabledBuildScenesExist()
        {
            var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).ToArray();
            Assert.That(scenes, Is.Not.Empty, "활성 빌드 씬이 필요합니다.");
            foreach (var scene in scenes)
                Assert.That(File.Exists(scene.path), Is.True, scene.path);
        }

        [Test]
        public void EnabledScenesHaveNoMissingScripts()
        {
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                foreach (var entry in EditorBuildSettings.scenes.Where(scene => scene.enabled))
                {
                    var scene = EditorSceneManager.OpenScene(entry.path, OpenSceneMode.Single);
                    foreach (var root in scene.GetRootGameObjects())
                        foreach (var child in root.GetComponentsInChildren<Transform>(true))
                            Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject),
                                Is.Zero, entry.path + ": " + child.name);
                }
            }
            finally
            {
                if (setup.Length > 0)
                    EditorSceneManager.RestoreSceneManagerSetup(setup);
            }
        }
    }
}
