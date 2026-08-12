#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ReachyMini.RemoteVlm.Tests
{
    internal static partial class Program
    {
        private static void Run(Action test)
        {
            test();
            caseCount = checked(caseCount + 1);
        }

        private static async Task RunAsync(Func<Task> test)
        {
            await test().ConfigureAwait(false);
            caseCount = checked(caseCount + 1);
        }

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
                    name + " expected '" + expected +
                    "' but received '" + actual + "'.");
            }
        }

        private static void Contains(
            string expected,
            string actual,
            string name)
        {
            if (!actual.Contains(expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    name + " expected text '" + expected + "'.");
            }
        }

        private static void Throws<TException>(
            Func<object?> action,
            string name)
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
