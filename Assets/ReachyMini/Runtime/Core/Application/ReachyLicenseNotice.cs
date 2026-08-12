#nullable enable

using System;

namespace ReachyMini.AppState
{
    public sealed class ReachyLicenseNotice
    {
        public ReachyLicenseNotice(
            string component,
            string attribution,
            string licenseReference)
        {
            if (string.IsNullOrWhiteSpace(component))
            {
                throw new ArgumentException(
                    "A license notice requires a component.",
                    nameof(component));
            }
            if (string.IsNullOrWhiteSpace(attribution))
            {
                throw new ArgumentException(
                    "A license notice requires attribution.",
                    nameof(attribution));
            }
            if (string.IsNullOrWhiteSpace(licenseReference))
            {
                throw new ArgumentException(
                    "A license notice requires a license reference.",
                    nameof(licenseReference));
            }

            Component = component;
            Attribution = attribution;
            LicenseReference = licenseReference;
        }

        public string Component { get; }

        public string Attribution { get; }

        public string LicenseReference { get; }
    }
}
