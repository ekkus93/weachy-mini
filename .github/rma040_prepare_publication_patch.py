from pathlib import Path

path = Path(".github/rma040_publication_fix.py")
text = path.read_text(encoding="utf-8")
replacements = (
    (
        """                authoritativeStateReader: null,
                commandQueueCapacity,
                maximumCommandBytes)
""",
        """                authoritativeStateReader: null,
                commandQueueCapacity,
                maximumCommandBytes,
                useAuthoritativeStateReader: false)
""",
    ),
    (
        """                commandQueueCapacity,
                maximumCommandBytes)
        {
        }

        private ReachySimulationWorker(
""",
        """                commandQueueCapacity,
                maximumCommandBytes,
                useAuthoritativeStateReader: true)
        {
        }

        private ReachySimulationWorker(
""",
    ),
    (
        """            int commandQueueCapacity,
            int maximumCommandBytes)
        {
            this.session = session ??
                throw new ArgumentNullException(nameof(session));
            this.authoritativeStateReader = authoritativeStateReader;
            authoritativeStateFrame = authoritativeStateReader?.CreateFrame();
""",
        """            int commandQueueCapacity,
            int maximumCommandBytes,
            bool useAuthoritativeStateReader)
        {
            this.session = session ??
                throw new ArgumentNullException(nameof(session));
            this.authoritativeStateReader = useAuthoritativeStateReader
                ? authoritativeStateReader
                : null;
            authoritativeStateFrame = this.authoritativeStateReader?.CreateFrame();
""",
    ),
)
for old, new in replacements:
    if text.count(old) != 1:
        raise SystemExit(f"Could not locate publication patch preparation block: {old[:60]!r}")
    text = text.replace(old, new, 1)
path.write_text(text, encoding="utf-8", newline="\n")
