#nullable enable

using System;
using System.Collections.Generic;

namespace ReachyMini.Camera.Tests
{
    internal static partial class Rma110VisionProviderContracts
    {
        private static void Contains(
            string value,
            string expected,
            string label)
        {
            if (!value.Contains(expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Managed test failed for {label}: '{value}' lacks '{expected}'.");
            }
        }

        private static void True(bool value, string label)
        {
            if (!value)
            {
                throw new InvalidOperationException(
                    $"Managed test failed for {label}: expected true.");
            }
        }

        private static void False(bool value, string label)
        {
            if (value)
            {
                throw new InvalidOperationException(
                    $"Managed test failed for {label}: expected false.");
            }
        }

        private static void Equal<T>(T expected, T actual, string label)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    $"Managed test failed for {label}: expected {expected}, found {actual}.");
            }
        }

        private static void Throws<TException>(Action action, string label)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }
            throw new InvalidOperationException(
                $"Managed test failed for {label}: expected {typeof(TException).Name}.");
        }
    }
}
