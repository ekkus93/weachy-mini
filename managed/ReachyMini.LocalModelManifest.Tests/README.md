# RMA-131 local model manifest managed contracts

This executable test project validates the immutable C# contract used by later local-model
management work. It intentionally does not load a GGUF file, select a model, download content, or
invoke llama.cpp.

The suite covers schema/runtime identity, provenance, experimental labeling, artifact path/hash
integrity metadata, normalized GGUF metadata, chat-template and stop-token bounds, memory-estimate
assumptions, Android/runtime compatibility, exact catalog lookup, duplicate rejection, and absence
of a hidden fallback model.

Run with:

```bash
dotnet run --project managed/ReachyMini.LocalModelManifest.Tests/ReachyMini.LocalModelManifest.Tests.csproj --configuration Release
```
