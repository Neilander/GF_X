using System;
using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;

[InitializeOnLoad]
public static class FlowColdStartRegressionCapture
{
    private const string ReportKey = "Avenge.FlowColdStartRegression.Report";

    static FlowColdStartRegressionCapture()
    {
        TestRunnerApi.RegisterTestCallback(new Capture());
    }

    public static void Arm(string reportName)
    {
        if (string.IsNullOrWhiteSpace(reportName) || Path.GetFileName(reportName) != reportName)
            throw new ArgumentException("Regression report name must be a filename.", nameof(reportName));
        SessionState.SetString(ReportKey, Path.GetFullPath(Path.Combine("Reports", reportName)));
    }

    private sealed class Capture : ICallbacks
    {
        public void RunStarted(ITestAdaptor testsToRun) { }
        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor result) { }
        public void RunFinished(ITestResultAdaptor result)
        {
            string path = SessionState.GetString(ReportKey, string.Empty);
            if (path.Length == 0)
                return;
            TestRunnerApi.SaveResultToFile(result, path);
            SessionState.EraseString(ReportKey);
        }
    }
}
