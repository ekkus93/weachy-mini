#nullable enable

using System;
using System.Net;
using ReachyMini.Diagnostics;

namespace ReachyMini.Providers
{
    public enum ReachyProviderHttpErrorCategory
    {
        None = 0,
        Authentication = 1,
        RateLimited = 2,
        Client = 3,
        Server = 4,
        Transport = 5,
    }

    public static class ReachyProviderDiagnosticMapping
    {
        public static ReachyProviderHttpErrorCategory Categorize(
            HttpStatusCode statusCode)
        {
            int value = (int)statusCode;
            if (value == 401 || value == 403)
            {
                return ReachyProviderHttpErrorCategory.Authentication;
            }
            if (value == 429)
            {
                return ReachyProviderHttpErrorCategory.RateLimited;
            }
            if (value >= 500 && value <= 599)
            {
                return ReachyProviderHttpErrorCategory.Server;
            }
            if (value >= 400 && value <= 499)
            {
                return ReachyProviderHttpErrorCategory.Client;
            }
            return ReachyProviderHttpErrorCategory.None;
        }

        public static ReachyDiagnosticErrorCategory ToDiagnosticCategory(
            ReachyProviderHttpErrorCategory category)
        {
            return category == ReachyProviderHttpErrorCategory.Transport
                ? ReachyDiagnosticErrorCategory.Network
                : category == ReachyProviderHttpErrorCategory.None
                    ? ReachyDiagnosticErrorCategory.None
                    : ReachyDiagnosticErrorCategory.Provider;
        }
    }
}
