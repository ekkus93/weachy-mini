#nullable enable

using System;

internal static partial class Program
{
    internal static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
