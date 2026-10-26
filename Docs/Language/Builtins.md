# Built-in types and functions

Minamo provides a small set of core values, types, and functions to every script. The `minamo`
console also provides terminal input and display functions. An embedding host chooses which
modules and commands to expose.

This page is a practical guide to the built-ins that are part of the current runtime. See the
[grammar reference](../Reference/Grammar.md) for syntax and the [Hosting API guide](../Developers/HostingGuide.md)
for controlling a script's environment.

## Core values and types

The core runtime includes these commonly used values and types:

| Value or type | Purpose |
| --- | --- |
| `nil` / `Nil` | Absence of a value |
| `true`, `false` / `Bool` | Bool values |
| `Integer`, `Float`, `Char`, `String`, `ByteArray` | Primitive values |
| `Json` | JSON parsing and serialization |
| `Array`, `Tuple`, `Dictionary`, `Set` | Collection values |
| `Iterator` | Lazy or bounded sequences, including ranges |
| `Option` | A value that may be present (`Some`) or absent (`None`) |
| `Result` | A successful value (`Ok`) or an error value (`Err`) |
| `Exception` | A tagged error value that can be thrown and caught |

Collections support indexing, iteration, and type-specific methods. `Dictionary` preserves the
insertion order of its entries, including when it is iterated, formatted, or converted to a tuple.
The [Syntax](Syntax.md) guide introduces collection literals, and [Functions and closures](FunctionsAndClosures.md)
covers iterators.

## Common type operations

Core types expose operations as methods. The following are useful starting points rather than a
complete API catalog.

| Type | Selected operations |
| --- | --- |
| `String` | `Length()`, `Split(...)`, `Trim()`, `Lower()`, `Upper()`, `Replace(...)`, `StartsWith(...)`, `EndsWith(...)` |
| `Array` | `Length()`, `Add(...)`, `Remove(...)`, `Sort()`, `Reverse()`, `ToSet()` |
| `Dictionary` | `Add(key, value)`, `TryAdd(key, value)`, `TryGet(key)`, `Remove(key)`, `Keys()` |
| `Set` | `Add(...)`, `Remove(...)`, `UnionWith(...)`, `IntersectWith(...)`, `ToArray()` |
| `Iterator` | `Map(...)`, `Filter(...)`, `Reduce(...)`, `Any(...)`, `All(...)`, `Take(...)`, `Skip(...)`, `ToArray()` |
| `Integer` / `Float` | `Parse(...)`, conversion helpers, and numeric bounds such as `Integer.Max` |

```swift
let names = ["ada", "lin", "mira"]
let upper = names.Map(name => name.Upper()).ToArray()

mut settings = ["theme": "dark"]
let theme = settings.TryGet("theme") ?? "system"
```

`TryGet` returns the stored value or `nil`, so it works naturally with `??`.

`Dictionary.Keys()` returns an `Iterator` over the current keys in insertion order, including
non-string keys. Keys are retained by reference. Enumeration starts when the iterator is consumed;
adding or removing entries during enumeration raises `CollectionModified`. Use `Keys().ToArray()`
to collect the keys before changing the dictionary.

## Option

`Option` represents an optional value. Construct `Some(value)` when a value is available and use
`None` when it is absent.

```swift
func FindUserName(id) {
    id == 42 ? Some("Ada") : None
}

let name = FindUserName(42) ?? "No name"
```

`??` unwraps `Some(value)` and returns its inner value. When its left side is `None` or `nil`, it
evaluates and returns the fallback expression.

Use a pattern when each case has different control flow:

```swift
if FindUserName(42) is Some(name) {
    print(fmt("Hello, {0}", name))
} else {
    print("No user found")
}
```

`match` is useful when each case produces a value:

```swift
let greeting = match FindUserName(42) {
    Some(name) => fmt("Hello, {0}", name),
    None => "Hello, guest"
}
```

## Result

`Result` represents an operation that either succeeds with `Ok(value)` or fails with `Err(error)`.
Unlike `Option`, `??` does not unwrap a `Result`; inspect it with `match`.

```swift
func ParsePort(text): Result<Integer, String> {
    let value = Integer.Parse(text)
    value is nil ? Err("A port number is required") : Ok(value)
}

let message = match ParsePort("8080") {
    Ok(port) => fmt("Listening on {0}", port),
    Err(error) => fmt("Configuration error: {0}", error)
}
```

`Result<Integer, String>` is a descriptive type annotation: the first argument documents the
success value and the second documents the error value. Type arguments are retained for tooling
and documentation; execution currently uses the outer `Result` type name.

## Exceptions

Use `Exception<Tag>(message)` to create a tagged exception. Throw it with `throw` and handle it
with `try` / `catch` / `finally`.

```swift
func RequireName(name) {
    if name == "" {
        throw Exception<UserError>("A user name is required")
    }

    name
}

try {
    print(RequireName(""))
} catch error {
    print(fmt("[{0}] {1}", error.Name, error.Message))
}
```

## Core functions

These functions are available without importing a module.

| Function | Description |
| --- | --- |
| `print(values..., separator: ",", terminator: "\n")` | Writes values to the script's output stream. |
| `fmt(template, values...)` | Formats a template with numbered placeholders such as `{0}`. |
| `assert(expected, actual, errorText?)` | Raises an assertion failure when the values differ. A one-argument assertion expects `true`. |
| `typeName(value)` | Returns the runtime type name of a value. |
| `constructorName(value)` | Returns a constructor name such as `Some` or `Ok`, or `nil` when the value has none. |
| `referenceEquals(left, right)` | Tests whether two values are the same runtime object. |
| `isCallable(value)` | Tests whether a value can be called. |
| `caller()` | Returns the calling function when one is available; otherwise returns `nil`. |
| `request(kind, payload?)` | Yields an input request from a select action and returns the host response. |

```swift
print(fmt("{0} + {1} = {2}", 2, 3, 2 + 3))
assert(5, 2 + 3)
print(typeName(Some("Ada")))
print(constructorName(Ok(42)))
```

`print` writes through an instance-specific output stream when the host supplies one. Otherwise it
uses the process console. See [Hosting API guide](../Developers/HostingGuide.md#host-input) for
host-provided input values.

`request` is valid only while a select action is running. It
suspends that action without blocking a thread. The host receives the kind and optional payload
through `MinamoSelectRequest`, then resumes the action with `MinamoSelect.RespondAsync`.

## Console functions

The `minamo` console provides `readLine()` and the `Console` type directly in scripts and the REPL.
No import is required. `readLine()` and `Console.ReadLine()` read one line from `Console.In` and
return an empty string at end of input. They do not consume Hosting input configured with
`UseInputAsync`.

```swift
print(readLine(), terminator: nil)
```

These functions belong to the console host. Applications embedding `Minamo.dll` define their own
input commands or use `host.Input()` with a configured input source. There is no `readline` module.

## Data formats

`ByteArray` and `Json` are core types available without an import. They provide explicit conversions
for common data formats:

```swift
let bytes = ByteArray.FromString("Minamo")
print(bytes.ToHex())

let value = Json.Parse("""{"ready":true,"ports":[8080,8081]}""")
print(Json.Stringify(value, indented: true))
```

## Next steps

- [Syntax](Syntax.md)
- [Operators](Operators.md)
- [Types and traits](TypesAndTraits.md)
- [Hosting API guide](../Developers/HostingGuide.md)
