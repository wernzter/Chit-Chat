using ChitChat.Core;
using ChitChat.Dictionary;
using ChitChat.Speech;

namespace ChitChat.App;

/// <summary>Smoke checks for the distribution package.</summary>
public static class SelfTest
{
    /// <summary>Runs package checks and returns a process exit code.</summary>
    public static int Run()
    {
        var failures = 0;
        Verify("correction and boundary handling", CheckDictionary, ref failures);
        Verify("local settings and history", CheckLocalFiles, ref failures);
        Verify("absolute model directories", () => ParakeetTranscriber.DefaultSearchPaths().All(Path.IsPathFullyQualified),
               ref failures);
        if (OperatingSystem.IsWindows()) Verify("Windows integration resolves and constructs", CheckPlatform, ref failures);
        return failures == 0 ? 0 : 1;
    }

    private static void Verify(string name, Func<bool> check, ref int failures)
    {
        try
        {
            if (check())
            {
                Console.WriteLine($"OK  {name}");
                return;
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.GetType().Name);
        }
        Console.Error.WriteLine($"FAIL  {name}");
        failures++;
    }

    private static bool CheckDictionary()
    {
        var correction = new DictionaryCorrector([DictionaryEntry.Correction("lake house", "Lakehouse")]);
        var result = correction.Apply("Lake-House at LakeHouseworks");
        return result.Text == "Lakehouse at LakeHouseworks" && result.Applied.Count == 1;
    }

    private static bool CheckLocalFiles()
    {
        var folder = Directory.CreateTempSubdirectory("chitchat-smoke-");
        try
        {
            var settingsPath = Path.Combine(folder.FullName, "settings.json");
            var prefs = new AppSettings(settingsPath);
            prefs.Update(prefs.Data with { InjectText = false });

            var historyPath = Path.Combine(folder.FullName, "transcripts.jsonl");
            var history = new TranscriptStore(historyPath);
            const string utterance = "A short smoke check.";
            history.Add(new TranscriptRecord { Text = utterance });
            return !new AppSettings(settingsPath).Data.InjectText
                   && new TranscriptStore(historyPath).Records.Single().Text == utterance;
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    private static bool CheckPlatform()
    {
        if (PlatformFactory.Current is not { } services) return false;

        using var trigger = services.CreateTrigger(0xA3);
        var capture = services.CreateCapture();
        try
        {
            return services.CreateOutput() is not null && services.CreateOpenAiKeyStore() is not null && !capture.IsCapturing;
        }
        finally
        {
            capture.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
