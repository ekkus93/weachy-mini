using System;

namespace ReachyMini.Presentation
{
    internal static class ReachyPinnedJointDebugMap
    {
        public const string SourceModelSha256 =
            "efd7e49d4288e5ef53945771a1f116584aa2c8b89721b725d5d77da9f0fcbf46";

        public const int NamedJointCount = 16;

        public static void ValidateSourceModel(string sourceModelSha256)
        {
            if (!string.Equals(
                sourceModelSha256,
                SourceModelSha256,
                StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The pinned joint debug map does not match the generated " +
                    "Reachy source-model SHA-256.");
            }
        }

        public static string[] CreateJointNames(string bodyName)
        {
            switch (bodyName)
            {
                case "body_down_3dprint":
                    return new[] { "yaw_body" };
                case "dc15_a01_horn_dummy":
                    return new[] { "stewart_1" };
                case "stewart_link_rod":
                    return new[] { "passive_1" };
                case "dc15_a01_horn_dummy_2":
                    return new[] { "stewart_2" };
                case "stewart_link_rod_2":
                    return new[] { "passive_2" };
                case "dc15_a01_horn_dummy_3":
                    return new[] { "stewart_3" };
                case "stewart_link_rod_3":
                    return new[] { "passive_3" };
                case "dc15_a01_horn_dummy_4":
                    return new[] { "stewart_4" };
                case "stewart_link_rod_4":
                    return new[] { "passive_4" };
                case "dc15_a01_horn_dummy_5":
                    return new[] { "stewart_5" };
                case "stewart_link_rod_5":
                    return new[] { "passive_5" };
                case "dc15_a01_horn_dummy_6":
                    return new[] { "stewart_6" };
                case "stewart_link_rod_6":
                    return new[] { "passive_6" };
                case "xl_330":
                    return new[] { "passive_7" };
                case "dc15_a01_horn_dummy_7":
                    return new[] { "right_antenna" };
                case "dc15_a01_horn_dummy_8":
                    return new[] { "left_antenna" };
                default:
                    return Array.Empty<string>();
            }
        }
    }
}
