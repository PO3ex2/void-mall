using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace VoidMall.Editor
{
    /// <summary>
    /// Запуск тестов из меню/скриптов с записью краткого отчёта в Temp/VoidMallTestResults.txt
    /// (удобно для автоматизации: PlayMode-тесты асинхронны).
    /// </summary>
    public static class TestRunnerBridge
    {
        public const string ReportPath = "Temp/VoidMallTestResults.txt";

        private sealed class Collector : ICallbacks
        {
            private readonly StringBuilder _failures = new();
            private int _passed;
            private int _failed;

            public void RunStarted(ITestAdaptor testsToRun)
            {
                File.WriteAllText(ReportPath, "RUNNING\n");
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                string summary = $"DONE passed={_passed} failed={_failed}\n{_failures}";
                File.WriteAllText(ReportPath, summary);
                Debug.Log($"[Tests] {summary}");
            }

            public void TestStarted(ITestAdaptor test) { }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (result.HasChildren)
                    return;
                if (result.TestStatus == TestStatus.Passed)
                {
                    _passed++;
                    return;
                }
                _failed++;
                _failures.AppendLine($"FAIL {result.FullName}\n  {result.Message?.Trim()}");
            }
        }

        [MenuItem("Void Mall/Tests/Run EditMode", priority = 100)]
        public static void RunEditMode() => Run(TestMode.EditMode, "VoidMall.Tests.EditMode");

        [MenuItem("Void Mall/Tests/Run PlayMode", priority = 101)]
        public static void RunPlayMode() => Run(TestMode.PlayMode, "VoidMall.Tests.PlayMode");

        private static void Run(TestMode mode, string assembly)
        {
            if (File.Exists(ReportPath))
                File.Delete(ReportPath);
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new Collector());
            api.Execute(new ExecutionSettings(new Filter { testMode = mode, assemblyNames = new[] { assembly } }));
        }
    }
}
