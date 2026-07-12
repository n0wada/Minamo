# Compatibility

This document defines the runtime compatibility contract for embedding Spellkit. It describes what
the repository currently builds and tests; it does not infer compatibility from a host product's
marketing name or approximate .NET support.

## Current targets

| Component | Target framework | Contract |
| --- | --- | --- |
| `Spellkit` | `net10.0` | Compiler, runtime, and Hosting API |
| `Spellkit.Console` | `net10.0` | Command-line host and standard modules |
| `Spellkit.UnitTests` | `net10.0` | Standard xUnit contract tests |
| `Spellkit.UnitTests` with `LanguageRunner=true` | `net10.0` | `.kit` language corpus report runner |
| `Spellkit.Generators` | `netstandard2.0` | Compiler-loaded source generators |

An application embedding the current `Spellkit.dll` must provide a runtime compatible with its
`net10.0` target. The Generator target does not lower the runtime requirement of generated bindings;
it only allows the compiler component to be loaded independently.

## Support levels

- **Validated:** Debug tests, Release build, Hosting contracts, and Generator contracts in this
  repository on the current target framework.
- **Candidate:** A named host and framework combination for which a build exists but the full test
  suite has not yet run inside the real host.
- **Supported:** A named host and framework combination with repeatable build, load, execution,
  Hosting, disposal, and Generator validation.
- **Unsupported:** Older framework targets, engine-specific runtimes, trimming, and Native AOT until
  they have an explicit compatibility target and test path.

## Adding a target

Add a target framework only for a concrete embedding host. Before calling it supported, verify:

1. `Spellkit.dll` and generated bindings load in the host.
2. Parsing, compilation, execution, cancellation, limits, and disposal pass there.
3. Host commands, state, resources, signals, and telemetry cross the boundary correctly.
4. The optional library remains separate so a restricted host can omit it.
5. Platform-specific APIs stay outside the core assembly or are guarded by the target.

Prefer the smallest target set backed by real hosts. Multi-targeting without a host test matrix
creates an apparent compatibility promise that the project cannot verify.

## Validation suites

The local test script exposes independent validation layers:

```powershell
.\scripts\test-local.ps1 -Suite Pipeline
.\scripts\test-local.ps1 -Suite Hosting
.\scripts\test-local.ps1 -Suite Generator
.\scripts\test-local.ps1 -Suite Language
.\scripts\test-local.ps1 -Suite Security
.\scripts\test-local.ps1 -Suite All
```

`Pipeline` checks parser diagnostics, lowering/compiler output, and direct VM execution. `Language`
exposes each `.kit` file as an xUnit test case and also runs the standalone corpus runner to update
the Markdown report. `All` runs every contract layer and the language corpus. All suites support
standard `dotnet test` discovery and filtering; Debug and Release use the same exception-handling
path in the language test runner.

`Security` runs focused regressions for interop overload matching, numeric bounds, path and symbolic
link escapes, cancellation and deadlines, exception sanitization, resource ownership, malformed
bytecode, and large source input.

The standalone language runner accepts `-Region` and `-TimeoutSeconds` through
`scripts/test-local.ps1`. Each region has its own execution deadline, and failures report the
file/region, elapsed time, expected and actual values, stack trace, and reproduction command.

`scripts/coverage-local.ps1` collects Coverlet line and branch coverage without a pass/fail
threshold. Its summary tracks Parser, Compiler, Runtime/Interop, Hosting, and Linker/FileLookup as
separate focus areas so the baseline can improve incrementally.
