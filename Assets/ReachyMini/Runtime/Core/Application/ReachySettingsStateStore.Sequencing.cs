#nullable enable

using System;

namespace ReachyMini.AppState
{
    public sealed partial class ReachySettingsStateStore
    {
        private static string NextString(string[] values, string currentValue)
        {
            for (int index = 0; index < values.Length; ++index)
            {
                if (string.Equals(
                        values[index],
                        currentValue,
                        StringComparison.Ordinal))
                {
                    return values[(index + 1) % values.Length];
                }
            }
            return values[0];
        }

        private static int NextInt(int[] values, int currentValue)
        {
            for (int index = 0; index < values.Length; ++index)
            {
                if (values[index] == currentValue)
                {
                    return values[(index + 1) % values.Length];
                }
            }
            return values[0];
        }

        private static bool Contains(string[] values, string? value)
        {
            if (value == null)
            {
                return false;
            }
            for (int index = 0; index < values.Length; ++index)
            {
                if (string.Equals(values[index], value, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool Contains(int[] values, int value)
        {
            for (int index = 0; index < values.Length; ++index)
            {
                if (values[index] == value)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
