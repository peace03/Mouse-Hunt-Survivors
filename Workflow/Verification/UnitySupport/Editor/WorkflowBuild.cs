using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MidnightMouse.Workflow.Editor
{
    // 검증 복사본에서만 컴파일되는 명령줄 빌드 진입점.
    public static class WorkflowBuild
    {
        [Serializable]
        private sealed class Plan
        {
            public string[] scenes;
        }

        [Serializable]
        private sealed class Proof
        {
            public string nonce;
            public string candidate_digest;
            public string stage;
            public bool success;
            public string unity_version;
            public string build_target;
            public string[] scenes;
            public string error;
            public int total_errors;
            public int total_warnings;
        }

        public static void Compile()
        {
            Write(new Proof { stage = "compile", success = !EditorUtility.scriptCompilationFailed });
        }

        public static void Build()
        {
            RunBuild(false);
        }

        public static void Prepare()
        {
            RunBuild(true);
        }

        private static void RunBuild(bool preparation)
        {
            var proof = new Proof { stage = preparation ? "prepare" : "player_build", build_target = "StandaloneWindows64" };
            try
            {
                var plan = JsonUtility.FromJson<Plan>(File.ReadAllText(Required("MH_WORKFLOW_PLAN")));
                if (plan.scenes == null || plan.scenes.Length == 0)
                    throw new InvalidOperationException("빌드 씬이 없습니다.");
                foreach (var scene in plan.scenes)
                    if (!File.Exists(scene))
                        throw new FileNotFoundException("빌드 씬을 찾을 수 없습니다.", scene);

                proof.scenes = plan.scenes;
                var output = Path.Combine(Required("MH_WORKFLOW_RUN"), preparation ? "PreparationPlayer" : "Player", "MidnightMouse.exe");
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = plan.scenes,
                    locationPathName = output,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.Development | BuildOptions.StrictMode
                });
                proof.total_errors = report.summary.totalErrors;
                proof.total_warnings = report.summary.totalWarnings;
                proof.success = report.summary.result == BuildResult.Succeeded && File.Exists(output);
                if (!proof.success)
                    proof.error = report.summary.result.ToString();
                if (preparation && proof.success)
                {
                    // 같은 define 집합의 저장 순서 차이도 입력 동결 전에 해소한다.
                    var symbols = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone).Split(';');
                    Array.Sort(symbols, StringComparer.Ordinal);
                    PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Standalone, symbols);
                    AssetDatabase.SaveAssets();
                }
            }
            catch (Exception exception)
            {
                proof.error = exception.ToString();
                Debug.LogException(exception);
            }
            Write(proof);
            if (!proof.success)
                EditorApplication.Exit(1);
        }

        private static string Required(string name)
        {
            return Environment.GetEnvironmentVariable(name)
                ?? throw new InvalidOperationException("검증 환경 변수가 없습니다: " + name);
        }

        private static void Write(Proof proof)
        {
            proof.nonce = Required("MH_WORKFLOW_NONCE");
            proof.candidate_digest = Required("MH_WORKFLOW_CANDIDATE");
            proof.unity_version = Application.unityVersion;
            File.WriteAllText(Path.Combine(Required("MH_WORKFLOW_RUN"), proof.stage + ".json"),
                JsonUtility.ToJson(proof, true));
        }
    }
}
