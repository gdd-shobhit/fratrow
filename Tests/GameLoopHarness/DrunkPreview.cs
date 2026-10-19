using System;
using System.Globalization;
using FratRow.Game;

/// <summary>
/// Calibration aid for drunk speech: prints a line as it comes out at each drunk level, using the live
/// <c>Assets/Resources/Campus/GameLoopConfig.json</c> tuning (edit the "drunk" section, run again).
/// <code>
/// dotnet run --project Tests/GameLoopHarness -- --drunk-speech "want to come to our party saturday?" [--samples 3] [--seed 1] [--cosmetic]
/// </code>
/// </summary>
static class DrunkPreview
{
    public static void Run(string[] args)
    {
        var config = Content.Config(new JsonCodec());
        var t = config.drunk;
        string text = Program.Arg(args, "--drunk-speech") ?? "Hey, are you coming to our party this Saturday? It's going to be so sick.";
        int samples = int.TryParse(Program.Arg(args, "--samples"), out int n) ? Math.Max(1, n) : 3;
        int seed = int.TryParse(Program.Arg(args, "--seed"), out int s) ? s : 1;
        bool cosmetic = Array.IndexOf(args, "--cosmetic") >= 0;
        Console.WriteLine("typed: " + text);
        Console.WriteLine("threshold " + t.threshold + ", wasted " + t.wastedAt + ", max " + t.max + (cosmetic ? " (button-line mode)" : ""));
        float[] levels = { t.threshold - 0.01f, t.threshold, (t.threshold + t.wastedAt) / 2, t.wastedAt, (t.wastedAt + t.max) / 2, t.max };
        foreach (float drunk in levels)
        {
            float k = DrunkSpeech.Intensity(drunk, t);
            Console.WriteLine();
            Console.WriteLine("drunk " + drunk.ToString("0.00", CultureInfo.InvariantCulture) + "  intensity " + k.ToString("0.00", CultureInfo.InvariantCulture));
            for (int i = 0; i < samples; i++)
                Console.WriteLine("  " + DrunkSpeech.Rewrite(text, k, t.speech, GameRng.For(seed, "drunk-preview", (long)(drunk * 1000), i), cosmetic));
        }
    }
}
