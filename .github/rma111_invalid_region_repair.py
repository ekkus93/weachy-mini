#!/usr/bin/env python3

from pathlib import Path


def replace_once(text: str, old: str, new: str, contract: str) -> str:
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{contract}: expected one source pattern, found {count}")
    return text.replace(old, new, 1)


def repair_acceptance() -> None:
    path = Path(
        "Assets/ReachyMini/Runtime/Application/ReachyRma111TrackingAcceptance.cs")
    text = path.read_text(encoding="utf-8")
    old_call = '''            byte[] invalidCenter = CreateValidity(
                fixture.Width,
                fixture.Height);
            InvalidateCenter(
                invalidCenter,
                fixture.Width,
                fixture.Height,
                secondFace.Bounds);
            TrackingResult masked = await TrackFixtureAsync(
                tracker,
                selection,
                fixture,
                sourceSequence: 3UL,
                timestampNanoseconds: 1_100_000_000L,
                validity: invalidCenter,
                requestId: "rma111-invalid-center");'''
    new_call = '''            byte[] invalidFaceRegion = CreateValidity(
                fixture.Width,
                fixture.Height);
            InvalidateFaceRegion(
                invalidFaceRegion,
                fixture.Width,
                fixture.Height,
                secondFace.Bounds);
            TrackingResult masked = await TrackFixtureAsync(
                tracker,
                selection,
                fixture,
                sourceSequence: 3UL,
                timestampNanoseconds: 1_100_000_000L,
                validity: invalidFaceRegion,
                requestId: "rma111-invalid-center");'''
    text = replace_once(text, old_call, new_call, "invalid-region invocation")

    old_method = '''        private static void InvalidateCenter(
            byte[] validity,
            int width,
            int height,
            NormalizedVisionBounds bounds)
        {
            int centerX = Math.Min(
                width - 1,
                Math.Max(0, (int)Math.Floor(bounds.CenterX * width)));
            int centerY = Math.Min(
                height - 1,
                Math.Max(0, (int)Math.Floor(bounds.CenterY * height)));
            validity[centerY * width + centerX] = 0;
        }'''
    new_method = '''        private static void InvalidateFaceRegion(
            byte[] validity,
            int width,
            int height,
            NormalizedVisionBounds bounds)
        {
            double horizontalPadding = bounds.Width * 0.5;
            double verticalPadding = bounds.Height * 0.5;
            int left = Math.Max(
                0,
                (int)Math.Floor(
                    (bounds.Left - horizontalPadding) * width));
            int top = Math.Max(
                0,
                (int)Math.Floor(
                    (bounds.Top - verticalPadding) * height));
            int rightExclusive = Math.Min(
                width,
                (int)Math.Ceiling(
                    (bounds.Left + bounds.Width + horizontalPadding) * width));
            int bottomExclusive = Math.Min(
                height,
                (int)Math.Ceiling(
                    (bounds.Top + bounds.Height + verticalPadding) * height));
            if (rightExclusive <= left || bottomExclusive <= top)
            {
                throw new InvalidOperationException(
                    "The detected face did not produce a non-empty invalid region.");
            }

            int invalidWidth = rightExclusive - left;
            for (int y = top; y < bottomExclusive; ++y)
            {
                Array.Fill(
                    validity,
                    (byte)0,
                    (y * width) + left,
                    invalidWidth);
            }
        }'''
    text = replace_once(text, old_method, new_method, "invalid-region method")
    path.write_text(text, encoding="utf-8")


def repair_source_contract() -> None:
    path = Path(
        "managed/ReachyMini.Camera.Tests/Rma111AndroidBridgeSourceContracts.cs")
    text = path.read_text(encoding="utf-8")
    old = '''            RequireText(
                acceptance,
                "second_person_id",
                "second person report ID");
            RequireText(
                script,
                "\\\"stable_person_id\\\"",
                "stable person shell gate");'''
    new = '''            RequireText(
                acceptance,
                "second_person_id",
                "second person report ID");
            RequireText(
                acceptance,
                "private static void InvalidateFaceRegion(",
                "detector-jitter-tolerant invalid face region");
            RequireText(
                acceptance,
                "Array.Fill(\\n                    validity,\\n                    (byte)0,",
                "multi-pixel invalid face region fill");
            RejectText(
                acceptance,
                "validity[centerY * width + centerX] = 0;",
                "brittle single-pixel physical invalidation");
            RequireText(
                script,
                "\\\"stable_person_id\\\"",
                "stable person shell gate");'''
    text = replace_once(text, old, new, "invalid-region source contract")

    marker = '''        private static void RequireText(
            string source,
            string expected,
            string contract)
        {
            if (!source.Contains(expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Managed RMA-111 source contract failed: {contract}.");
            }
        }'''
    replacement = marker + '''

        private static void RejectText(
            string source,
            string rejected,
            string contract)
        {
            if (source.Contains(rejected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Managed RMA-111 source contract failed: {contract}.");
            }
        }'''
    text = replace_once(text, marker, replacement, "RejectText helper")
    path.write_text(text, encoding="utf-8")


def verify() -> None:
    acceptance = Path(
        "Assets/ReachyMini/Runtime/Application/ReachyRma111TrackingAcceptance.cs"
    ).read_text(encoding="utf-8")
    required = (
        "InvalidateFaceRegion(",
        "double horizontalPadding = bounds.Width * 0.5;",
        "double verticalPadding = bounds.Height * 0.5;",
        "Array.Fill(\n                    validity,\n                    (byte)0,",
    )
    for expected in required:
        if expected not in acceptance:
            raise SystemExit(f"Acceptance repair marker missing: {expected}")
    if "validity[centerY * width + centerX] = 0;" in acceptance:
        raise SystemExit("Brittle single-pixel invalidation remains")
    if acceptance.count("InvalidateFaceRegion(") != 2:
        raise SystemExit("InvalidateFaceRegion must have one call and one declaration")

    contract = Path(
        "managed/ReachyMini.Camera.Tests/Rma111AndroidBridgeSourceContracts.cs"
    ).read_text(encoding="utf-8")
    if contract.count("private static void RejectText(") != 1:
        raise SystemExit("RejectText source contract helper is not unique")


def main() -> None:
    repair_acceptance()
    repair_source_contract()
    verify()


if __name__ == "__main__":
    main()
