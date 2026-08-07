# ReachyMini.LocalModelPackages.Tests

Deterministic RMA-132 package-management contracts. The suite uses only synthetic byte arrays, temporary directories, an injected storage probe, and an injected download transport. It performs no external network request and downloads no real model.

The cases cover verified import, exact SHA-256/size rejection, storage preflight, fresh and resumed download, clean restart, wrong range rejection, tampered installed artifacts, exact deletion, termination recovery, orphan cleanup, store ownership, provenance-bound download origins, and source-change restart behavior.

Run with:

```bash
dotnet run --project managed/ReachyMini.LocalModelPackages.Tests/ReachyMini.LocalModelPackages.Tests.csproj --configuration Release
```
