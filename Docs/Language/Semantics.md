# Semantics

This guide collects behavior that affects how Spellkit code evaluates. It complements the syntax
guides; the [grammar reference](../Reference/Grammar.md) remains the source for accepted syntax.

## Expressions and values

Most language forms produce a value. The last expression in a function block is the function's
value, and `if`, `guard`, loops, and `match` can be used as expressions where their result is
needed.

```swift
let status = if ready {
    "ready"
} else {
    "waiting"
}

func absolute(value) {
    if value < 0 {
        -value
    } else {
        value
    }
}
```

`return` exits a function early. A value supplied to `break` becomes the value of the loop
expression.

## Bindings and mutation

`let` creates an immutable binding and `mut` creates a mutable binding. Assignment requires a
mutable target. `const` declares a named constant.

`const` is limited to global (module) scope. Use `let` rather than `const` for an immutable value
inside a function or other local scope.

```swift
let name = "Ada"
mut attempts = 0
const MaximumAttempts = 3

attempts += 1
```

`use` owns a value for the current scope and disposes it when that scope exits.

```swift
use file = File.Create("output.txt")
```

## Nil and conditional evaluation

`nil` represents the absence of a value. Use `??` to choose a fallback when its left-hand
expression is `nil` or `None`; when the expression is `Some(value)`, `??` returns the inner value.
Use `??=` to assign a fallback to a mutable location.

```swift
let displayName = name ?? "anonymous"
let selectedId = Some(42) ?? 0
mut message = nil
message ??= "welcome"
```

`&&` and `||` combine logical conditions. Use an `if` expression or the conditional operator when
one of two values should be selected.

## Patterns and matching

Patterns test and decompose values. Lowercase names bind values, while uppercase bare names denote
types or nullary constructors. `_` is a fallback pattern.

```swift
let selection = Some(42)
let label = match selection {
    Some(value) => fmt("selected: {0}", value),
    None => "nothing selected"
}
```

`Some(value)` is a constructor pattern: `Some` identifies the case and `value` binds the value it
carries. The binding exists only while that pattern's branch is evaluated.

The same constructor pattern can be used in an `if` condition. Its value binding exists only in
the successful branch.

```swift
func SelectedId() => Some(42)

if SelectedId() is Some(id) {
    print(id)
} else {
    print("No selection")
}
```

Use `match` when both `Some` and `None` are normal outcomes and each needs its own value.

Patterns may be used with `match` and with the `is` operator. A `when` clause adds a condition to
a match entry.

## Type annotations

Type annotations retain information for tooling and documentation, but they are not a separate
static type system. The compiler and runtime do not enforce them consistently.

```swift
let count: Integer = 3
mut value: String? = nil
let result: Result<String, String> = Ok("ready")
```

`T?` is shorthand for `T | Nil`. For a parameterized hint such as `Result<String, String>`,
execution currently uses the outer `Result` name while tooling retains both arguments.

## Names and host boundaries

Within a hosted script, local names, imports, and built-ins are resolved before names exposed by
the host environment. This allows one compiled program to run with different host-provided views.

Hosts can also hide the global `host` object and expose only explicitly selected names. See the
[Hosting API guide](../Hosting/Guide.md) for capabilities, resource lifetimes, execution limits,
and the host boundary.

## Exceptions and cleanup

`throw` raises a value. `try`, `catch`, and `finally` provide structured error handling; `finally`
runs after either a successful or failed attempt. Use `use` when a value must be disposed as its
scope ends.

```swift
try {
    loadConfiguration()
} catch error {
    print(error.Message)
} finally {
    closeConnection()
}
```

## Next steps

- [Program structure](ProgramStructure.md)
- [Syntax](Syntax.md)
- [Hosting API guide](../Hosting/Guide.md)
- [Grammar reference](../Reference/Grammar.md)
