using System;
using System.Globalization;
using UnityEngine;

public static class LogicDeterminismCorpusRunner
{
    public const string CommandLineArgument = "-runLogicDeterminismCorpus";
    public const string SuccessMarker = "AVENGE_LOGIC_DETERMINISM_CORPUS_PASS";
    public const string FailureMarker = "AVENGE_LOGIC_DETERMINISM_CORPUS_FAIL";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RunBeforeSceneLoad()
    {
#if AVENGE_DETERMINISM_CORPUS_RUNNER
        const bool dedicatedRunner = true;
#else
        bool dedicatedRunner = HasCommandLineArgument(CommandLineArgument);
#endif
        if (!dedicatedRunner)
            return;

        try
        {
            LogicDeterminismCorpusResult result = LogicDeterminismCorpus.ValidateV65();
            Debug.Log(string.Format(
                CultureInfo.InvariantCulture,
                "{0} corpus={1} protocol={2} content={3} events={4} checksum={5} inputHash={6} timeHash={7} fullHash={8}",
                SuccessMarker,
                LogicDeterminismCorpus.CorpusVersion,
                result.ProtocolVersion,
                result.ContentVersion,
                result.EventCount,
                result.InputChecksum,
                result.InputHash,
                result.TimeControlHash,
                result.FullHash));
            Application.Quit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Debug.LogError($"{FailureMarker} corpus={LogicDeterminismCorpus.CorpusVersion} error={exception.Message}");
            Application.Quit(1);
        }
    }

    private static bool HasCommandLineArgument(string expected)
    {
        string[] arguments = Environment.GetCommandLineArgs();
        for (int i = 0; i < arguments.Length; i++)
        {
            if (string.Equals(arguments[i], expected, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
