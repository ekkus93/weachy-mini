#nullable enable

using System;
using System.Collections.Generic;

namespace ReachyMini.Diagnostics
{
    public static class ReachyDiagnosticBundleManifest
    {
        private static readonly ReachyDiagnosticDataClass[] DeniedClasses =
        {
            ReachyDiagnosticDataClass.Secret,
            ReachyDiagnosticDataClass.PrivateText,
            ReachyDiagnosticDataClass.RawAudio,
            ReachyDiagnosticDataClass.RawImage,
            ReachyDiagnosticDataClass.RawMedia,
        };

        public static IReadOnlyList<ReachyDiagnosticDataClass> DefaultDeniedDataClasses =>
            Array.AsReadOnly(DeniedClasses);

        public static bool IsIncludedByDefault(ReachyDiagnosticDataClass dataClass)
        {
            return dataClass == ReachyDiagnosticDataClass.Public ||
                dataClass == ReachyDiagnosticDataClass.Identifier ||
                dataClass == ReachyDiagnosticDataClass.Url ||
                dataClass == ReachyDiagnosticDataClass.Header;
        }
    }
}
