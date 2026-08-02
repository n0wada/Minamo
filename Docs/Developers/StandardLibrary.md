# Standard library policy

Spellkit distinguishes the portable standard library from host services and extended libraries.
The distinction is about API responsibility, not only about whether an implementation happens to
use a managed .NET assembly.

## Admission criteria

A module belongs in the portable standard library when all of the following are true:

- It provides broadly useful, foundational data processing rather than an application framework.
- It can be implemented with Spellkit and the .NET base class library, without an additional DLL
  or native component.
- It uses one assembly on every supported OS and exposes substantially the same public semantics.
- Its API is small enough to support as a stable part of the language distribution.
- It does not require access to the file system, console, network, process, environment, registry,
  GUI, or another host resource for its primary purpose.

Nondeterminism alone does not exclude a module. For example, UUID generation and a future seeded
pseudo-random module are portable, but their APIs must make nondeterministic behavior explicit.

## Library layers

The console distribution currently has three registration layers:

| Layer | Responsibility | Current modules |
| --- | --- | --- |
| Standard | Portable, foundational values and data processing | `binary`, `collections`, `json`, `math`, `random`, `text`, `time`, `uuid` |
| Host | Basic access to resources supplied by the running host | `console`, `io` |
| Extended | Higher-level features scheduled to become separately loaded libraries | `http` |

`AddStandardLibrary`, `AddHostLibrary`, and `AddExtendedLibrary` represent these boundaries inside
`Spellkit.Console`. The `spell` executable composes all three with `AddBundledLibraries` while the
extended-library manifest and loading mechanism are being developed. Consequently this
classification does not yet remove a previously available module from the command-line program.
The host and extended registration layers build on standard-library types, so internal callers add
them after `AddStandardLibrary`; `AddBundledLibraries` applies that ordering automatically.

### Standard modules

- `binary` owns byte arrays and portable binary encodings.
- `collections` owns collection types whose behavior is materially different from the core
  `Array`, `Tuple`, `Dictionary`, and `Set` types.
- `json` owns strict conversion between JSON text and Spellkit's primitive and collection values.
- `math` owns general numeric functions and constants.
- `random` owns independent pseudo-random generators, including reproducible seeded generators.
- `text` owns text-processing types such as regular expressions and string builders.
- `time` owns dates, times, durations, UTC values, and fixed offsets. APIs that use the local time
  zone are environment-sensitive and should be clearly identified; named time-zone databases may
  later require a host or extended module.
- `uuid` owns UUID values, parsing, formatting, and generation.

The `time` module uses `DateTime` exclusively for UTC wall-clock values. `DateTime.UtcNow()` is the
only clock-reading operation on that type. `LocalDateTime` combines local clock fields with a fixed
UTC offset; `LocalDateTime.Now()` and a missing constructor offset consult the system time zone.
`ToLocal`, `FromUtc`, and `ToUtc` make transitions between the two domains explicit. Named time-zone
rules remain outside the portable API.

### Host modules

Host modules are bundled with the console because they are basic scripting facilities, but they are
not portable data-processing modules. An embedding application may omit them or replace them with
narrower commands.

- `console` reads from the instance environment or process console.
- `io` accesses files, directories, paths, drives, and platform-defined file attributes.

### Extended and external libraries

A module belongs outside the standard library when any of the following applies:

- It requires an additional managed or native DLL.
- It needs a different build or binary for a supported OS.
- It exposes an OS-specific subsystem or substantially different behavior between platforms.
- It is a high-level convenience package likely to grow through protocol, provider, or format
  options.
- Its dependency or release cadence should not be tied to the Spellkit runtime.

The existing `http` module is the first externalization candidate. GUI frameworks, database
drivers, image and media processing, archive formats, cryptography integrations, process control,
and OS-specific services also belong in separately versioned libraries.

## Dependency rule

Standard and host modules may depend on the Spellkit runtime and the .NET base class library.
Extended libraries should depend on Spellkit's public runtime-extension surface, not on the
`Spellkit.Console` executable. If an external library must exchange a standard-library foreign type
such as `ByteArray` or a date/time value directly, that type may eventually need to move to a small
shared class-library assembly. Until that need is demonstrated, the modules remain in
`Spellkit.Console`.

## Review questions

Before admitting a module to the standard library, answer these questions in its proposal:

1. Is the capability foundational for scripts in multiple application domains?
2. Can its observable behavior be specified without referring to one OS or host environment?
3. Can it remain dependency-free beyond the Spellkit runtime and .NET base class library?
4. Is the proposed API deliberately small and supportable as a stable contract?
5. Would an embedding host reasonably want to expose it without granting an external-resource
   capability?

If the answer to the last question is no, the module is normally a host or extended library rather
than part of the portable standard library.

## Public naming conventions

Library APIs follow the naming already established by Spellkit's core types:

| API element | Convention | Examples |
| --- | --- | --- |
| Module name or path segment | lower camel case | `math`, `collections` |
| Top-level function or value | lower camel case | `sqrt`, `readLine`, `typeName` |
| Type, constructor, or trait | PascalCase | `ByteArray`, `SortedDictionary`, `Session` |
| Instance or static member | PascalCase | `Length`, `TryGet`, `RaiseForStatus` |
| Parameter or named argument | lower camel case | `baseUrl`, `allowRedirects` |
| Tuple label or data field | lower camel case | `statusCode`, `keyChar` |

Initialisms use ordinary word casing inside identifiers: `Http`, `Json`, `Uuid`, and `Url` in
PascalCase names, or `http`, `json`, `uuid`, and `url` at the start of lower-camel-case names.
Protocol data keeps the spelling supplied by that protocol; JSON object keys, for example, are not
rewritten.

A function that constructs a library type uses the type's PascalCase name, while ordinary module
operations remain lower camel case. When a natural lower-camel-case operation is a Spellkit
keyword, the complete, closely related API family may use PascalCase rather than mixing styles.
The HTTP request family follows this exception because `get` is reserved: `Get(...)`, `Post(...)`,
and `Request(...)` are module operations, `Session(...)` constructs a session, and
`session.Get(...)` is an instance method.
