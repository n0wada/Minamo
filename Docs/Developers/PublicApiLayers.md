# Public API layers

Minamo's public API is divided by purpose. Application code should normally import only
`Minamo.Hosting`; the other layers are opt-in surfaces for tools and runtime extensions.

| Layer | Namespaces | Intended use |
| --- | --- | --- |
| Application API | `Minamo.Hosting` | Create a host and instance, register commands, execute source or files, and consume results |
| Tooling API | `Minamo.Parser`, `Minamo.Parser.Model`, `Minamo.Compiler`, `Minamo.Linker`, `Minamo.Debug`, `Minamo.Codegen` | Parse and inspect syntax trees, compile or link units, inspect bytecode, and build debuggers or language tools |
| Runtime extension API | `Minamo.Runtime`, `Minamo.Runtime.Types`, `Minamo.Runtime.Interop`, and `Minamo` | Implement native runtime values, foreign units, and integrations that deliberately participate in VM semantics |
| Internal implementation | Non-public types | VM stacks and dispatch machinery, compiler implementation state, host registries, and process/environment helpers |

## Application API

Most applications need only:

```csharp
using Minamo.Hosting;

var host = new MinamoHost();
using var instance = host.CreateInstance();

var result = await instance.ExecuteAsync("40 + 2");
```

Application code does not need to construct a parser, linker, compiler unit, runtime context, or
evaluation stack. Typed result, signal, state, and command helpers keep ordinary host code outside
the runtime object model. `MinamoEnvironment.UseOutput` routes `print` output, while
`UseInputAsync` supplies input to the optional `readline` library.

Interactive UIs use `MinamoInstance.OpenSelectAsync` and the live `MinamoSelect` application
surface. `MinamoSelectProperty` publishes script-defined read-only values, while
`MinamoSelectDescription` and `MinamoSelectMetadata` expose free-form string-key dictionaries
through typed host conversion. Language-level `case` declarations appear as `MinamoChoice`
objects in `MinamoSelect.Choices`; hosts select the published object rather than parsing or
invoking script syntax directly.

Compiler and parser results expose a fixed `Messages` snapshot together with filtered `Errors` and
`Warnings` lists. Use `TryGetValue(out var value)` for normal branching or `GetValueOrThrow()` when
a failed build should become a `MinamoBuildException`.

## Tooling API

The Tooling API is intentionally public. Types such as `MinamoParser`, syntax nodes, `Op`, `OpCode`,
`Unit`, and debug symbols are the data model used by formatters, analyzers, disassemblers,
debuggers, and custom build pipelines. They are not required to execute scripts through Hosting.

For common parser entry points, use `MinamoParser.Parse(source, sourceName)` or
`MinamoParser.ParseFile(path)`. Construct a `SourceBuffer` only when a custom source abstraction is
needed. Tooling that must avoid blocking during file I/O can await
`SourceBuffer.FromFileAsync(path, cancellationToken)` and pass the resulting buffer to
`MinamoParser.Parse`.

For common compilation, use `MinamoCompiler.Compile(source)` or
`MinamoCompiler.CompileFile(path)`. These overloads use a restricted lookup, so imports require
an explicitly configured `FileLookup`. Advanced pipelines can use `MinamoLinker` directly; its
`BuilderOptions` always come from the supplied lookup and cannot be specified a second time.

File-based tooling can choose its lookup scope explicitly. `FileLookup.Standard(options)` searches
relative to the importing file and in `MINAMO_LIBS`; `FileLookup.Restricted(options)` searches
only paths added by the caller. Neither mode searches beside the Minamo executable.

Tooling consumers should import only the specific namespaces they use. Tooling contracts can
evolve separately from the application-facing Hosting contract.

## Runtime extension API

The Runtime extension API is for integrations that implement Minamo values or participate
directly in execution. `RuntimeContext`, `ExecutionContext`, `MinamoObject`, runtime types, and
interop conversion belong here. This layer assumes knowledge of VM lifetime and error semantics.

Prefer generated Hosting commands and opaque resources unless direct runtime participation is
actually required.

## Internal implementation

Implementation-only types are non-public. In particular, evaluation stacks, VM dispatch state,
compiler implementation contexts, host registries, culture globals, and executable-path probing
are not extension points.

The API-boundary tests reject exported types outside the three public namespace groups and verify
representative contracts in each group. A new public type must therefore be assigned deliberately
to Application, Tooling, or Runtime extension API.
