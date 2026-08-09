# Built-in types and functions

Spellkit provides a small set of core values, types, and functions to every script. The `spell`
console also registers the standard-library modules shipped with the executable. An embedding host
can choose which modules and commands to expose, so a script must not assume that console modules
are available in every hosted environment.

This page is a practical guide to the built-ins that are part of the current runtime. See the
[grammar reference](../Reference/Grammar.md) for syntax and the [Hosting API guide](../Developers/HostingGuide.md)
for controlling a script's environment.

## Core values and types

The core runtime includes these commonly used values and types:

| Value or type | Purpose |
| --- | --- |
| `nil` / `Nil` | Absence of a value |
| `true`, `false` / `Boolean` | Boolean values |
| `Integer`, `Float`, `Char`, `String`, `ByteArray` | Primitive values |
| `Json` | JSON parsing and serialization |
| `Array`, `Tuple`, `Dictionary`, `Set` | Collection values |
| `Range`, `Iterator` | Lazy or bounded sequences |
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

These functions are available without importing a standard-library module.

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

```swift
print(fmt("{0} + {1} = {2}", 2, 3, 2 + 3))
assert(5, 2 + 3)
print(typeName(Some("Ada")))
print(constructorName(Ok(42)))
```

`print` writes through an instance-specific output stream when the host supplies one. Otherwise it
uses the process console. See [Hosting API guide](../Developers/HostingGuide.md#instance-input-and-output)
for host-controlled input and output.

## Standard library modules

The `spell` console registers its standard library modules. Import a module before using its public
names. `ByteArray` and `Json` are core types and are available without an import.

```swift
import * from math

print(sqrt(81))
```

| Modules | Main capabilities |
| --- | --- |
| `collections` | Collection types beyond the core collection types. |
| `readline` | Console input through `readLine`. |
| `io` | Files, directories, paths, drives, and file attributes. |
| `math` | General numeric functions and constants. |
| `random` | Independent pseudo-random generators. |
| `text` | Text-processing types such as regular expressions and string builders. |
| `time` | UTC and local date-time values, durations, and fixed offsets. |
| `uuid` | UUID parsing, formatting, and generation. |

For example, `readline` provides input while `print` remains a core function:

```swift
import * from readline

print(readLine(), terminator: nil)
```

### `collections`

Import `collections` for collection types whose ordering or capacity has a specific meaning. All
five types are iterable and expose `Length()` and `Count`; operations that have no value to return
produce `nil`.

| Type | Purpose and selected operations |
| --- | --- |
| `PriorityQueue(values?)` | A minimum-priority queue. `Enqueue(value, priority)`, `Peek()`, and `Dequeue()`; the latter two return `(value, priority)`. Equal priorities retain enqueue order. |
| `Deque(values?)` | A double-ended queue. `PushFront(value)`, `PushBack(value)`, `PopFront()`, `PopBack()`, `First()`, and `Last()`. |
| `SortedSet(values?)` | A duplicate-free set iterated in ascending value order. `Add(value)`, `Remove(value)`, `Contains(value)`, `First()`, `Last()`, and `Range(...)`. |
| `MultiMap(pairs?)` | Multiple values per key. `Add(key, value)`, `Get(key)`, `Remove(key, value)`, `RemoveKey(key)`, `ContainsKey(key)`, and `Keys`. Keys and values retain insertion order. |
| `RingBuffer(capacity, values?)` | A fixed-size, oldest-to-newest buffer. `Add(value)` overwrites the oldest value when full; `First()`, `Last()`, and `Capacity` expose its current boundaries. |

`values` is a sequence. `PriorityQueue` expects `(value, priority)` pairs and `MultiMap` expects
`(key, value)` pairs. `Get` returns an `Array` snapshot, including an empty array when the key is
absent. `RingBuffer` requires a positive capacity.

```swift
import * from collections

let work = PriorityQueue()
work.Enqueue("write release notes", 2)
work.Enqueue("ship", 1)
let next = work.Dequeue()       // (value: "ship", priority: 1)

let recent = RingBuffer(3, [1, 2, 3])
recent.Add(4)
print(recent.ToArray())         // [2, 3, 4]
```

The `readline` and `io` modules can access host resources. An embedding host chooses which
modules to register and can instead expose narrower host commands and capabilities. The `http`
module is not part of the default `spell` distribution; add its external extension through
[`spellkit.json`](../Developers/HostingGuide.md#external-extension-libraries) when it is needed.

Core types provide explicit conversions for common data formats:

```swift
let bytes = ByteArray.FromString("Spellkit")
print(bytes.ToHex())

let value = Json.Parse("""{"ready":true,"ports":[8080,8081]}""")
print(Json.Stringify(value, indented: true))
```

Seeded random generators are independent and reproducible within the running Spellkit version:

```swift
import * from random

let generator = Random(42)
print(generator.Next(min: 1, max: 7))
print(generator.Shuffle(("red", "green", "blue")))
```

`DateTime` represents UTC values and reads the clock through `DateTime.UtcNow()`. `LocalDateTime`
stores local clock fields together with a fixed offset; use `ToLocal(...)`, `FromUtc(...)`, and
`ToUtc()` for explicit conversion. Only `LocalDateTime.Now()`, `LocalDateTime.SystemOffset`, and an
omitted local offset consult the system time zone.

## Next steps

- [Syntax](Syntax.md)
- [Operators](Operators.md)
- [Types and traits](TypesAndTraits.md)
- [Hosting API guide](../Developers/HostingGuide.md)
