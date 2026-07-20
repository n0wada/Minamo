# Syntax

This guide introduces the written form of Spellkit source code: how source is separated into
statements, how names and literals are written, and how optional type annotations are expressed.
For the complete parser grammar, see the [grammar reference](../Reference/Grammar.md).

## Source text and statements

Spellkit source is Unicode text. Whitespace and comments separate tokens. A statement ends at a
line break, a semicolon, a closing brace, or the end of the source file.

Use braces to form a block when several statements belong together:

```swift
if ready {
    print("Starting")
    start()
}
```

## Comments

Use `//` for a comment through the end of the line. Use `/* ... */` for a block comment; block
comments can be nested.

```swift
// A line comment

/*
   A block comment
   /* with a nested comment */
*/
```

## Identifiers and keywords

An identifier starts with `_` or a Unicode letter. Later characters may also be digits. A qualified
name joins two identifiers with a dot.

```swift
let user_name = "Ada"
let locale = app.Settings
```

Keywords have a defined role in the language and cannot be used as ordinary identifiers. The
current keyword set includes `let`, `mut`, `func`, `if`, `for`, `match`, `struct`, `enum`, `trait`,
`impl`, `import`, `return`, `throw`, and `yield`.

## Numbers

`Integer` values use signed 64-bit storage. `Float` values use .NET `double` precision. An
underscore may separate digits for readability.

```swift
42
1_000_000
0xFF_E0
3.14159
.5
1.2E+10
```

The `f` or `F` suffix is accepted on floating-point literals, but it does not reduce their
precision.

## Strings and characters

Use double quotes for strings and single quotes for one UTF-16 character. Triple-quoted strings
preserve text across multiple lines. Adjacent ordinary strings are concatenated.

```swift
let greeting = "Hello, " "Spellkit"
let initial = 'S'
let path = "C:\\work\\spellkit"
let text = """
Line one
Line two
"""
```

The supported escapes are `\s`, `\t`, `\r`, `\n`, `\b`, `\"`, `\'`, `\\`, `\0`, and Unicode
escapes such as `\uFFFF`.

## Literal values and collections

The simple literals are `nil`, `true`, `false`, numbers, strings, and characters. Spellkit also
has arrays, tuples, labeled tuples, and dictionaries.

```swift
let values = [1, 2, 3]
let pair = (1, "two")
let record = (name: "Ada", age: 33)
let settings = ["theme": "dark", "retries": 3]
```

Parentheses around one unlabeled expression only group that expression. A comma or a label makes
the expression a tuple.

```swift
let grouped = (1 + 2)
let pair = (value, other)
let named = (value: 3)
```

Arrays and dictionaries can be built with comprehensions:

```swift
let squares = [value * value for value in 1..5]
let labels = [value: fmt("#{0}", value) for value in 1..3]
let enabled = [item for item in items when item.Enabled]
```

## Type annotations

Type annotations describe intent for tooling and documentation; they are not a separate static
type system and are not consistently enforced at compile time or runtime.

```swift
let count: Integer = 3
mut value: String? = nil
func load(): Result<String, String> => Ok("ready")
func showAgain(String value) => value
```

Use `?` for a nullable type and `|` for a union. `String?` is shorthand for `String | Nil`.
Parameterized hints retain their arguments in the syntax tree, while execution currently uses only
the outer type name.

```swift
let message: String | Nil = nil
let result: Result<String, String> = Ok("ready")
```

The arguments in `Result<String, String>` describe the success and error values for readers and
tooling. They do not create separate runtime instantiations of `Result`.

## Next steps

- [Operators](Operators.md)
- [Program structure](ProgramStructure.md)
- [Grammar reference](../Reference/Grammar.md)
