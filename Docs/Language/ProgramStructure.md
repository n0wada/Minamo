# Program structure

This guide describes how a Spellkit program is organized: bindings, control flow, modules, and
error handling. For the exact grammar, see the [grammar reference](../Reference/Grammar.md).

## Bindings and constants

Use `let` for an immutable binding and `mut` for a binding that may be reassigned. Use `const` for
a named constant. `use` disposes its value when the current scope exits.

> `const` declarations are permitted only at global (module) scope. A `const` declaration inside
> a function or another local scope is a compile-time error; use `let` for a local immutable value.

```swift
let name = "Ada"
mut score = 0
const MaxAttempts = 3
use file = File.Create("output.txt")

score += 10
```

An uninitialized constant receives its own name as a string value.

```swift
const DefaultMode
print(DefaultMode) // "DefaultMode"
```

## Blocks and scope

Braces group statements into a block. Bindings declared in a block belong to that scope.

```swift
if enabled {
    let message = "ready"
    print(message)
}
```

Functions, structs, enums, traits, and implementations provide the program's reusable behavior.
Their declarations are covered in [Functions and closures](FunctionsAndClosures.md) and
[Types and traits](TypesAndTraits.md).

## Conditional flow

`if` selects a branch and can be used as either a statement or an expression.

```swift
let description = if score > 0 {
    "positive"
} else {
    "zero or negative"
}
```

`guard` executes its block when its condition is false.

```swift
guard connection.IsOpen {
    return nil
}
```

## Loops

Use `while`, `do ... while`, or `for` to repeat work. A `for` loop can filter values with `when`
and run an `else` block when no matching iteration executes.

```swift
mut index = 0
while index < items.Length() {
    process(items[index])
    index += 1
}

for item in items when item.Enabled {
    process(item)
} else {
    print("No enabled items")
}
```

`break` exits a loop and `continue` begins its next iteration. A value passed to `break` becomes
the value of the loop expression.

## Return, yield, and throw

Use `return` to finish a function and optionally provide its value. `yield` produces values from
an iterator function; a function containing `yield` is an iterator function. Use `throw` to raise
an error value.

```swift
func first(values) {
    if values.Length() == 0 {
        return nil
    }

    values[0]
}

func numbers() {
    for value in 1..3 {
        yield value
    }
}
```

## Pattern matching

`match` chooses an expression by comparing a value with ordered patterns. Patterns can match
literals, ranges, constructors, and a fallback `_`. A `when` clause adds a condition to a match
entry.

```swift
enum Maybe { None, Some(value) }

let user = Maybe.Some("Ada")
let description = match user {
    Some(name) => fmt("Signed in as {0}", name),
    None => "No signed-in user"
}
```

In `Some(name)`, `Some` selects the enum case and `name` binds the value stored in that case. The
binding is available only in that match entry.

Use `is Some(name)` when control flow depends on whether an `Option` has a value:

```swift
func FindUserName() => Some("Ada")

if FindUserName() is Some(name) {
    print(name)
} else {
    print("No name")
}
```

The `name` binding is available only in the `Some` branch. Use `??` when both cases should produce
a single value instead:

```swift
let name = FindUserName() ?? "No name"
```

## Modules and visibility

Use `import` to bring a module into the current file. An import may have an alias, select one
name, or import every public name. Imports are local and are not re-exported.

```swift
import modules/pricing as pricing
import receiptLine from modules/text
import * from modules/common
```

Module declarations are public by default. Prefix a module-level binding, constant, function,
struct, enum, or trait with `private` to keep it within its module.

```swift
private const InternalName = "spellkit"
private func normalize(value) => value.Trim()
```

## Exceptions

Use `try`, `catch`, and `finally` to handle a thrown value. `Exception<Tag>(...)` creates a tagged
Spellkit exception.

```swift
try {
    loadUser("Ada")
} catch error {
    print(error.Message)
} finally {
    closeConnection()
}
```

## Regions

`#region` and `#endregion` name a region of source. They are primarily used by the `.kit` test
corpus to identify independent test cases.

```swift
#region "basic arithmetic"
1 + 2
#endregion
```

## Next steps

- [Functions and closures](FunctionsAndClosures.md)
- [Types and traits](TypesAndTraits.md)
- [Semantics](Semantics.md)
- [Grammar reference](../Reference/Grammar.md)
