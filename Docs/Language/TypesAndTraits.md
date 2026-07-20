# Types and traits

This guide covers Spellkit's nominal data types, traits, and implementations. Type annotations are
descriptive metadata rather than a separate static type system; see [Syntax](Syntax.md) for their
written form and the [grammar reference](../Reference/Grammar.md) for the complete syntax.

## Structs

Use `struct` to declare a nominal data type. Its fields define the generated constructor. Fields
are read-only by default; prefix an individual field with `mut` when it must be writable.

```swift
struct Point {
    mut x: Float,
    y: Float
}

let point = Point(10.0, 20.0)
point.x = 15.0
```

Fields can have defaults and type annotations.

```swift
struct Connection {
    host: String,
    mut retries: Integer = 0
}
```

## Enums

Use `enum` to declare named alternatives. A case may be nullary or carry values with the same
field syntax as a struct.

```swift
enum Result {
    Ok(value: String),
    Err(error: String)
}

let result = Ok("ready")
```

`Option` models a value that may be present or absent. `Some(value)` carries one value; `None`
carries no value.

```swift
let name = Some("Ada")
```

Enum cases work naturally with [pattern matching](ProgramStructure.md#pattern-matching).

```swift
let message = match result {
    Ok(value) => value,
    Err(error) => error
}

let greeting = match name {
    Some(value) => fmt("Hello, {0}", value),
    None => "Hello, guest"
}
```

Use `??` to unwrap a present value and supply a fallback when a function returns `None`.

```swift
func FindUserName() => Some("Ada")

let name = FindUserName() ?? "No name"
print(fmt("Hello, {0}", name))
```

Use `is Some(value)` when the two cases need different control flow. The `value` binding is
available only in the `Some` branch.

```swift
if FindUserName() is Some(name) {
    print(fmt("Hello, {0}", name))
} else {
    print("Hello, guest")
}
```

Use a type annotation when it clarifies the values that an enum represents. For example,
`Result<String, String>` documents a string success value and a string error value. Spellkit
retains both arguments for tooling and documentation, while runtime execution uses the outer
`Result` type name.

```swift
let loaded: Result<String, String> = Ok("ready")
let failed: Result<String, String> = Err("not found")
```

## Traits

A trait declares required behavior without providing function bodies. Types can then state that
they conform to the trait through an implementation.

```swift
trait Displayable {
    func Describe()
}
```

## Implementations

Use `impl` to add behavior to a declared type. An implementation can provide methods, internal
state, an `init` function, properties, and conformance to one or more traits.

```swift
impl Point with Displayable {
    mut cached

    func init(x, y) {
        this.cached = nil
    }

    func Describe() => fmt("{0}:{1}", this.x, this.y)
}
```

Inside an implementation, `this` is the current value.

## Qualified functions

A qualified function adds behavior to a type or a module. This is useful when a small operation
does not need an `impl` block.

```swift
func Integer.Double() => this * 2
func library.Widget.Show() => this.ToString()
```

## Properties and indexers

Use `get` and `set` on a qualified function to declare a property. An indexer accepts one or more
parameters in brackets.

```swift
func get Array.First() => this[0]
func set Array.First(value) { this[0] = value }
func get Grid[x, y] => this.values[y][x]
```

## Operators and conversions

Types can provide custom operators and conversions. Operator declarations place the operator after
the type; conversions use `as` between source and target types.

```swift
func Point + (other) => Point(this.x + other.x, this.y + other.y)
func Point as String => fmt("{0}:{1}", this.x, this.y)
```

See [Operators](Operators.md) for operator behavior, precedence, and the special rules for `<<`
and `>>`.

## Next steps

- [Functions and closures](FunctionsAndClosures.md)
- [Program structure](ProgramStructure.md)
- [Operators](Operators.md)
- [Grammar reference](../Reference/Grammar.md)
