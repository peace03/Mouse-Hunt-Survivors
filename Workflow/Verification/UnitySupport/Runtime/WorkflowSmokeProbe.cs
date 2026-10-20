#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MidnightMouse.Workflow
{
    // 명시적인 검증 인자가 있는 개발용 Player에서만 시작 상태를 기록한다.
    public sealed class WorkflowSmokeProbe : MonoBehaviour
    {
        [Serializable]
        private sealed class Plan { public int smoke_frames; }

        [Serializable]
        private sealed class Proof
        {
            public string nonce;
            public string candidate_digest;
            public string stage = "player_smoke";
            public bool success;
            public string scene;
            public int frames;
            public int errors;
            public string graphics_device;
        }

        private int _errors;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            if (Application.isEditor || !Environment.GetCommandLineArgs().Contains("--workflow-smoke"))
                return;
            var instance = new GameObject("WorkflowSmokeProbe");
            DontDestroyOnLoad(instance);
            instance.AddComponent<WorkflowSmokeProbe>();
        }

        private void Awake()
        {
            Application.logMessageReceived += OnLog;
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
        }

        private void OnLog(string message, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                _errors++;
        }

        private IEnumerator Start()
        {
            var plan = JsonUtility.FromJson<Plan>(File.ReadAllText(Environment.GetEnvironmentVariable("MH_WORKFLOW_PLAN")));
            for (var frame = 0; frame < plan.smoke_frames; frame++)
                yield return null;

            var scene = SceneManager.GetActiveScene();
            var proof = new Proof
            {
                nonce = Environment.GetEnvironmentVariable("MH_WORKFLOW_NONCE"),
                candidate_digest = Environment.GetEnvironmentVariable("MH_WORKFLOW_CANDIDATE"),
                scene = scene.path,
                frames = plan.smoke_frames,
                errors = _errors,
                graphics_device = SystemInfo.graphicsDeviceType.ToString(),
                success = scene.IsValid() && scene.isLoaded && _errors == 0
            };
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("MH_WORKFLOW_RUN"), "player_smoke.json"),
                JsonUtility.ToJson(proof, true));
            Application.Quit(proof.success ? 0 : 1);
        }
    }
}
#endif
