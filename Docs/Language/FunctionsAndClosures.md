# Functions and closures

This guide describes named functions, lambdas, closures, and iterator functions. For the exact
grammar, see the [grammar reference](../Reference/Grammar.md).

## Named functions

Declare a function with `func`. A function can use a block body or an expression body introduced
by `=>`.

```swift
func add(x, y) {
    x + y
}

func square(value) => value * value
```

The final expression of a block is its value, so a `return` is only needed for an early exit or
when it makes the intent clearer.

```swift
func first(values) {
    if values.Length() == 0 {
        return nil
    }

    values[0]
}
```

## Parameters and calls

Parameters may have type annotations, default values, or a final variadic capture. Calls may name
their arguments.

```swift
func greet(name: String = "world") => fmt("Hello, {0}", name)
func collect(values...) => values

greet()
greet(name: "Ada")
collect(1, 2, 3) // returns (1, 2, 3)
```

Type annotations describe intent and are not consistently enforced at compile time or runtime.

## Lambdas

A lambda is an anonymous function value. Use either a single parameter without parentheses or a
parenthesized parameter list.

```swift
let double = value => value * 2
let add = (left, right) => left + right

let values = [1, 2, 3]
let doubled = [double(value) for value in values]
```

## Closures

Closures can use bindings from their enclosing scope. This makes them useful for small stateful
operations and callbacks.

```swift
func makeCounter(start) {
    mut value = start
    () => {
        value += 1
        value
    }
}

let next = makeCounter(100)
print(next())
print(next())
```

## Functions as values

Functions and lambdas are first-class values: they can be passed to another function, returned,
and stored in a binding.

```swift
func apply(value, transform) => transform(value)

let withTax = amount => amount * 11 / 10
let total = apply(200, withTax)
```

## Iterators and yield

A function containing `yield` is an iterator function. Each `yield` produces the next value;
`yield break` ends the iterator early.

```swift
func squares() {
    for value in 1..5 {
        yield value * value
    }
}

for value in squares() {
    print(value)
}
```

## Methods and accessors

Functions can also be declared as methods, property accessors, indexers, conversions, and custom
operators. See [Types and traits](TypesAndTraits.md) for those declaration forms.

## Next steps

- [Program structure](ProgramStructure.md)
- [Types and traits](TypesAndTraits.md)
- [Language recipes](Recipes.md)
- [Grammar reference](../Reference/Grammar.md)
