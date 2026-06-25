using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Lobotom.Build.Editor
{
    public static class BuildCommand
    {
        public static void BuildWindows64()
        {
            // Автоматически берем все сцены из Build Settings
            string[] scenes = new string[EditorBuildSettings.scenes.Length];
            for (int i = 0; i < EditorBuildSettings.scenes.Length; i++)
            {
                scenes[i] = EditorBuildSettings.scenes[i].path;
                Debug.Log($"Scene {i}: {scenes[i]}");
            }

            BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = "build/StandaloneWindows64/Library.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);

            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log(" Build succeeded!");
            }
            else
            {
                Debug.LogError(" Build failed!");
                throw new System.Exception("Build failed with errors!");
            }
        }
    }
}