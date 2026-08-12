#nullable enable

using System;
using System.Collections.Generic;

namespace ReachyMini.LocalVlm.Tests
{
    internal static partial class Program
    {
        private static void True(bool value, string name)
        {
            if (!value)
            {
                throw new InvalidOperationException(name + " expected true.");
            }
        }

        private static void False(bool value, string name)
        {
            if (value)
            {
                throw new InvalidOperationException(name + " expected false.");
            }
        }

        private static void Equal<T>(T expected, T actual, string name)
            where T : notnull
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    name + " expected '" + expected + "' but received '" + actual + "'.");
            }
        }

        private static void Same(object expected, object? actual, string name)
        {
            if (!ReferenceEquals(expected, actual))
            {
                throw new InvalidOperationException(name + " expected the same instance.");
            }
        }

        private static void Contains(string expected, string actual, string name)
        {
            if (!actual.Contains(expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    name + " expected text '" + expected + "'.");
            }
        }

        private static void SetEqual(
            IEnumerable<string> expected,
            IEnumerable<string> actual,
            string name)
        {
            var expectedSet = new HashSet<string>(expected, StringComparer.Ordinal);
            var actualSet = new HashSet<string>(actual, StringComparer.Ordinal);
            if (!expectedSet.SetEquals(actualSet))
            {
                throw new InvalidOperationException(name + " sets do not match.");
            }
        }

        private static void Throws<TException>(Func<object?> action, string name)
            where TException : Exception
        {
            try
            {
                _ = action();
            }
            catch (TException)
            {
                return;
            }
            throw new InvalidOperationException(
                name + " expected " + typeof(TException).Name + ".");
        }
    }
}
