#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

internal static class Program
{
    private static async Task<int> Main()
    {
        IReadOnlyList<(string Name, Func<Task> Run)> tests = AndroidSystemTtsTests.All;
        int failures = 0;

        foreach ((string name, Func<Task> run) in tests)
        {
            try
            {
                await run().ConfigureAwait(false);
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine(
                    $"FAIL {name}: {exception.GetType().Name}: {exception.Message}");
            }
        }

        if (failures != 0)
        {
            Console.Error.WriteLine(
                $"RMA-124 Android system/network TTS contracts failed: {failures}/{tests.Count}.");
            return 1;
        }

        Console.WriteLine(
            $"RMA-124 Android system/network TTS contracts passed: {tests.Count}.");
        return 0;
    }
}
