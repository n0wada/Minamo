# Operators

This guide describes the expressions that combine, compare, transform, and assign values. For the
complete grammar, see the [grammar reference](../Reference/Grammar.md).

## Member access, indexing, and calls

Use `.` to access a member, `[]` to index a value, and `()` to call a function. These postfix
operations associate from left to right. Call arguments may be labeled.

```swift
let name = user.Profile.Name
let first = values[0]
let result = calculate(amount: 20, tax: 2)
```

## Arithmetic operators

Use the familiar arithmetic operators for numeric expressions.

```swift
let sum = left + right
let difference = left - right
let product = left * right
let quotient = left / right
let remainder = left % right
```

Unary `+` and `-` express a positive or negative value.

```swift
let balance = -amount
let normalized = +amount
```

## Comparison and logical operators

Comparison operators produce a Boolean value. Logical operators use doubled symbols and can be
combined with `!` for negation.

```swift
let same = left == right
let ordered = score >= minimum
let ready = enabled && count > 0
let missing = !value
let acceptable = value == "standard" || value == "priority"
```

Use `??` to provide a fallback when the expression on its left is `nil` or `None`. When the left
expression is `Some(value)`, `??` unwraps and returns `value`.

```swift
func FindUserName() => Some("Ada")

let name = FindUserName() ?? "No name"
```

## Ranges and pattern tests

`..` creates an inclusive range and `..<` excludes the upper bound. Ranges are useful with loops,
collections, and patterns.

```swift
let inclusive = 1..10
let exclusive = 1..<10
let digits = [value for value in 0..9]
```

Use `in` for membership and `is` to test a pattern.

```swift
if item in items {
    print("found")
}

if value is 1..9 {
    print("single digit")
}
```

## Conditional and conversion operators

The conditional operator selects one of two expressions. Use `as` where a conversion is supported
by the value or its type.

```swift
let label = enabled ? "enabled" : "disabled"
let text = value as String
```

## Assignment and rebinding

Use `=` to assign or rebind a mutable location. Spellkit also supports nil-coalescing and
arithmetic assignment.

```swift
mut total = 10
total += 5
total *= 2
total ??= 0
```

Plain assignment can destructure a tuple into existing mutable bindings.

```swift
mut (x, y) = (1, 2)
(x, y) = (10, 20)
```

## Custom operators

Types can define an operator by declaring a qualified function with the operator after the type.
The left operand becomes `this` inside the implementation.

```swift
func Point + (other) => Point(this.x + other.x, this.y + other.y)
```

`<<` and `>>` are overload-only operators. They do not perform built-in bit shifting, so the type
of the left operand must provide an implementation before either operator can be used.

```swift
func Pipeline << (other) => Pipeline(this.steps + other.steps)
```

## Precedence

The following table lists binary operators from lowest to highest precedence. Postfix access,
indexing, and calls bind more tightly than every binary operator.

| Level | Operators |
| --- | --- |
| conditional | `condition ? yes : no` |
| 1 | `??` |
| 2 | `\|\|` |
| 3 | `&&` |
| 4 | `in`, `is pattern` |
| 5 | `..`, `..<` |
| 6 | `==`, `!=`, `<`, `>`, `<=`, `>=` |
| 7 | `<<`, `>>` |
| 8 | `+`, `-` |
| 9 | `*`, `/`, `%` |
| 10 | `as Type` |

Binary operators are left-associative. Add parentheses when the intended grouping is not obvious.

## Next steps

- [Syntax](Syntax.md)
- [Program structure](ProgramStructure.md)
- [Types and traits](TypesAndTraits.md)
- [Grammar reference](../Reference/Grammar.md)
