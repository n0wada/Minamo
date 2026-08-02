# Public API layers

Spellkit's public API is divided by purpose. Application code should normally import only
`Spellkit.Hosting`; the other layers are opt-in surfaces for tools and runtime extensions.

| Layer | Namespaces | Intended use |
| --- | --- | --- |
| Application API | `Spellkit.Hosting` | Create a host and instance, register commands, execute source or files, and consume results |
| Tooling API | `Spellkit.Parser`, `Spellkit.Parser.Model`, `Spellkit.Compiler`, `Spellkit.Linker`, `Spellkit.Debug`, `Spellkit.Codegen` | Parse and inspect syntax trees, compile or link units, inspect bytecode, and build debuggers or language tools |
| Runtime extension API | `Spellkit.Runtime`, `Spellkit.Runtime.Types`, `Spellkit.Runtime.Interop`, and `Spellkit` | Implement native runtime values, foreign units, and integrations that deliberately participate in VM semantics |
| Internal implementation | Non-public types | VM stacks and dispatch machinery, compiler implementation state, host registries, and process/environment helpers |

## Application API

Most applications need only:

```csharp
using Spellkit.Hosting;

var host = new SpellkitHost();
using var instance = host.CreateInstance();

var result = instance.Execute("40 + 2");
```

Application code does not need to construct a parser, linker, compiler unit, runtime context, or
evaluation stack. Typed result, signal, state, and command helpers keep ordinary host code outside
the runtime object model. `SpellkitEnvironment` can supply instance-local input and output when
hosted scripts use `print`; console input is provided by the optional `readline` library.

Compiler and parser results expose a fixed `Messages` snapshot together with filtered `Errors` and
`Warnings` lists. Use `TryGetValue(out var value)` for normal branching or `GetValueOrThrow()` when
a failed build should become a `SpellkitBuildException`.

## Tooling API

The Tooling API is intentionally public. Types such as `SpellkitParser`, syntax nodes, `Op`, `OpCode`,
`Unit`, and debug symbols are the data model used by formatters, analyzers, disassemblers,
debuggers, and custom build pipelines. They are not required to execute scripts through Hosting.

For common parser entry points, use `SpellkitParser.Parse(source, sourceName)` or
`SpellkitParser.ParseFile(path)`. Construct a `SourceBuffer` only when a custom source abstraction is
needed. Tooling that must avoid blocking during file I/O can await
`SourceBuffer.FromFileAsync(path, cancellationToken)` and pass the resulting buffer to
`SpellkitParser.Parse`.

For common compilation, use `SpellkitCompiler.Compile(source)` or
`SpellkitCompiler.CompileFile(path)`. These overloads use a restricted lookup, so imports require
an explicitly configured `FileLookup`. Advanced pipelines can use `SpellkitLinker` directly; its
`BuilderOptions` always come from the supplied lookup and cannot be specified a second time.

Tooling consumers should import only the specific namespaces they use. Tooling contracts can
evolve separately from the application-facing Hosting contract.

## Runtime extension API

The Runtime extension API is for integrations that implement Spellkit values or participate
directly in execution. `RuntimeContext`, `ExecutionContext`, `SpellkitObject`, runtime types, and
interop conversion belong here. This layer assumes knowledge of VM lifetime and error semantics.

Prefer generated Hosting commands and opaque resources unless direct runtime participation is
actually required.

File-based tooling can choose its lookup scope explicitly. `FileLookup.Standard(options)` searches
relative to the importing file and in `SPELLKIT_LIBS`; `FileLookup.Restricted(options)` searches
only paths added by the caller. Neither mode searches beside the Spellkit executable.

## Internal implementation

Implementation-only types are non-public. In particular, evaluation stacks, VM dispatch state,
compiler implementation contexts, host registries, culture globals, and executable-path probing
are not extension points.

The API-boundary tests reject exported types outside the three public namespace groups and verify
representative contracts in each group. A new public type must therefore be assigned deliberately
to Application, Tooling, or Runtime extension API.
