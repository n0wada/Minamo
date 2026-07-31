# Spellkit grammar reference

This document describes the grammar accepted by the current handwritten parser. The parser and
the passing files under [`Spellkit.UnitTests/Tests`](../../Spellkit.UnitTests/Tests) remain
authoritative. See the [overview](../Language/Overview.md) for an introduction and the
[recipes](../Language/Recipes.md) for runnable examples.

## Notation

```text
name        ::= required production
[ item ]    ::= optional item
{ item }    ::= zero or more repetitions
item | item ::= alternatives
"text"      ::= literal source text
```

## Source text and separators

Source is Unicode. Whitespace and comments separate tokens. A statement ends at a line break,
semicolon, closing brace, or end of file.

```text
block ::= "{" { statement separator } "}"
```

## Comments

```text
line-comment  ::= "//" { non-line-break }
block-comment ::= "/*" { character | block-comment } "*/"
```

Block comments may be nested.

## Identifiers and keywords

Identifiers begin with `_` or a Unicode letter; later characters may include digits.

```text
identifier     ::= identifier-start { identifier-part }
qualified-name ::= identifier [ "." identifier ]
```

Reserved words include:

```text
as break catch continue do else false for from func get if import in is
let many match mut nil not or private return set static throw true try type
use when while with yield
```

`const`, `struct`, `enum`, `trait`, `impl`, `guard`, `finally`, `select`, `initial`, `state`,
`choose`, `on`, `goto`, `exit`, `label`, and `description` are contextual keywords.

## Numeric literals

```text
integer  ::= digits | ("0x" | "0X") hex-digits
fraction ::= digits "." digits | "." digits
exponent ::= ("e" | "E") [ "+" | "-" ] digits
float    ::= (fraction [ exponent ] | digits exponent) [ "f" | "F" ]
```

Underscores may separate digits. `Integer` is signed 64-bit; `Float` uses .NET `double`. The `f`
suffix does not reduce precision.

```swift
42
1_000_000
0xFF_E0
3.14159
.5
1.2E+10
```

## Strings and characters

```text
string           ::= '"' { character | escape } '"'
character        ::= "'" (character | escape) "'"
multiline-string ::= '"""' { character } '"""'
```

Escapes are `\s`, `\t`, `\r`, `\n`, `\b`, `\"`, `\'`, `\\`, `\0`, and `\uFFFF`.
Characters must decode to one UTF-16 character. Triple-quoted strings preserve their contents.
Adjacent ordinary strings are concatenated.

## Literals and collections

```text
literal ::= "nil" | "true" | "false" | integer | float | string | character
tuple   ::= "(" [ argument { "," argument } ] ")"
array   ::= "[" [ array-element { "," array-element } ] "]"
label   ::= (identifier | string) ":" expression
```

One unlabeled parenthesized expression is grouping. A comma or label creates a tuple.

```swift
()
(1, 2)
(name: "Ada", age: 33)
[1, 2, 3]
[name: "Ada", age: 33]
["long key": true]
```

Comprehensions are:

```text
"[" expression "for" pattern "in" expression [ "when" expression ] "]"
"[" key ":" value "for" pattern "in" expression [ "when" expression ] "]"
```

## Type annotations

Annotations are descriptive metadata, not a separate static type system. Spellkit does not
enforce them consistently at compile time or runtime.

```text
type-name          ::= identifier [ "." identifier ]
type-hint          ::= type-name [ "<" type-annotation { "," type-annotation } ">" ]
nullable-type-hint ::= type-hint [ "?" ]
type-annotation    ::= nullable-type-hint { "|" nullable-type-hint }
```

```swift
let count: Integer = 3
func show(value: String?) => value
func load(): Result<String> => Ok("ready")
func showAgain(String value) => value
```

`T?` is shorthand for `T | Nil`. Parameterized hints such as `Result<String>` retain their
type arguments in the syntax tree for tooling and documentation, but the compiler and runtime
currently treat them like the outer `Result` hint.

## Bindings and constants

```text
binding ::=
    ("let" | "mut") pattern [ ":" type-annotation ] [ "=" expression ]
  | "use" identifier [ ":" type-annotation ] "=" expression

constant ::=
    "const" constant-entry
  | "const" "{" constant-entry { "," constant-entry } "}"

constant-entry ::= identifier [ "=" expression ]
```

`let` is immutable, `mut` is mutable, and `use` disposes its value at scope exit. An uninitialized
constant receives its own name as a string.

## Functions

```text
function ::=
    [ "static" ] "func" function-signature (block | "=>" arrow-body)

function-signature ::=
    [ "get" | "set" ] function-name
    "(" [ parameter { "," parameter } ] ")"
    [ ":" type-annotation ]

parameter ::=
    [ type-annotation ] identifier
    [ ":" type-annotation ]
    [ "=" expression ]
    [ "..." ]
```

```swift
func add(x: Integer, y: Integer): Integer { x + y }
func greet(name = "world") => fmt("Hello, {0}", name)
func collect(values...) => values
```

The final expression of a block is its value.

## Interactive selects

An interactive select defines a host-driven state machine. A host normally opens it through
`SpellkitInstance.OpenSelect`; Script may also invoke a factory with `do expression`. The host renders
current choices and sends a selected choice back to the session. See
[Interactive selects](../Developers/InteractiveSelect.md) for the C# protocol.

```text
select-declaration
    ::= "select" [ identifier ] "{" select-local* state-declaration+ "}"

select-local
    ::= ( "let" | "mut" ) pattern "=" expression

state-declaration
    ::= [ "initial" ] "state" identifier
        "{" { choice-declaration | event-declaration } "}"

choice-declaration
    ::= "choose" string [ "(" identifier { "," identifier } ")" ]
        [ "label" string ]
        [ "description" string ]
        [ "when" expression ]
        "=>" choice-body

event-declaration
    ::= "on" string [ "(" identifier { "," identifier } ")" ]
        "=>" choice-body

choice-body
    ::= block | "goto" identifier | "exit" [ expression ]

goto-statement
    ::= "goto" identifier

exit-statement
    ::= "exit" [ expression ]

select-alias
    ::= "alias" "(" expression "," string ")"

select-invocation
    ::= "do" expression
```

Named select declarations are permitted only at global (module) scope. Exactly one state is marked
`initial`. Select locals are created for each select instance and must appear before the state
declarations. Without `goto`, an action remains in its current state. `goto` exposes the target
state directly, and a state without choices or events completes immediately. `exit` completes the
session. Choice and event names are unique within their respective channels in one state. Both receive either no
argument, one value, or a tuple whose elements bind to their parameters. `choose` declarations are
visible through `Choices`; `on` declarations are hidden and delivered through the host's `Send`
API. `label` and `description` provide host-facing display text; `when` controls whether a choice
is currently available. `goto` targets must name a state declared by the same select.
`do expression` invokes a factory and evaluates to its exit value.

```swift
select player {
    initial state stopped {
        choose "play" label "Play" when music.HasSelectedTrack() => {
            music.Play()
            goto playing
        }
    }

    state playing {
        choose "stop" => {
            music.Stop()
            goto stopped
        }

        choose "exit" => exit "done"
    }
}

alias(player, "music.player")
```

## Lambdas

```text
lambda ::=
    identifier "=>" expression
  | "(" [ parameter { "," parameter } ] ")" "=>" expression
```

```swift
let double = x => x * 2
let add = (x, y) => x + y
let traced = value => { print(value); value }
```

## Members, properties, indexers, and operators

Qualified functions add behavior to a type or module:

```swift
func Integer.Double() => this * 2
func library.Widget.Show() => this.ToString()
```

Properties and indexers use `get` and `set`:

```swift
func get Array.First() => this[0]
func set Array.First(value) { this[0] = value }
func get Grid[x, y] => this.values[y][x]
```

Conversions use `func Source as Target`, and operators place the operator after the type:

```swift
func Point + (other) => Point(this.x + other.x, this.y + other.y)
func Pipeline << (other) => Pipeline(this.steps + other.steps)
```

`<<` and `>>` are overload-only operators. They have no built-in bit-shift behavior; using
either operator requires an implementation on the left operand's type.

## Calls and postfix expressions

```text
postfix ::=
    primary
    { "." identifier
    | "[" expression "]"
    | "(" [ argument { "," argument } ] ")"
    }
```

Postfix operations associate left-to-right. Arguments may be labeled. `Exception<Tag>(...)` is a
special exception form; general generic calls are not supported.

## Operators and precedence

Unary operators are `!`, unary `+`, and unary `-`.

Binary operators are left-associative. Lowest precedence appears first:

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
| postfix | access, indexing, calls |

Logical operators use doubled symbols.

## Ranges

```text
range ::=
    [ expression ] (".." | "..<") [ expression ]
```

`..<` excludes the upper bound. Use `Iterator.Range(start, end, step, exclusive)` when a custom
step is required.

```swift
1..10
1..<10
..10
1..
Iterator.Range(0, 10, 2)
```

## Assignment and rebinding

```text
assignment-operator ::=
    "=" | "??=" | "+=" | "-=" | "*=" | "/=" | "%="
```

Plain `=` also supports destructuring rebinding:

```swift
mut (x, y) = (1, 2)
(x, y) = (10, 20)
```

## Conditional flow

```text
if-form    ::= "if" expression block [ "else" (if-form | block) ]
guard-form ::= "guard" expression block [ "else" (guard-form | block) ]
```

`guard condition { body }` executes `body` when the condition is false. Both forms may be used as
expressions.

## Loops

```text
while-loop    ::= "while" expression block
do-while-loop ::= "do" block "while" expression
select-invocation ::= "do" expression
for-loop      ::= "for" pattern "in" expression
                  [ "when" expression ] block [ "else" block ]
```

Loops support `break [expression]` and `continue`. A value passed to `break` becomes the loop
expression's result. A `for` `else` block runs if no matching iteration executes.

## Return, yield, and throw

```text
return-statement ::= "return" [ same-line-expression ]
break-statement  ::= "break" [ same-line-expression ]
throw-statement  ::= "throw" [ same-line-expression ]
yield-statement  ::= "yield" expression | "yield" "break"
```

A function containing `yield` is an iterator function.

## Patterns

```text
pattern       ::= or-pattern
or-pattern    ::= and-pattern { "or" and-pattern }
and-pattern   ::= range-pattern { "and" range-pattern }
range-pattern ::= primary-pattern [ ".." primary-pattern ]

primary-pattern ::=
    identifier | "_" | literal | "nil"
  | "not" primary-pattern
  | "(" pattern { "," pattern } ")"
  | "[" [ range-pattern { "," range-pattern } ] "]"
  | constructor-pattern

constructor-pattern ::=
    [ module "." ] [ type "." ] constructor
    "(" [ pattern { "," pattern } ] ")"
```

Lowercase names bind values. Uppercase bare names denote types or nullary constructors.

## Match

```text
match ::= "match" expression "{"
            match-entry { "," match-entry }
          "}"

match-entry ::= pattern [ "when" expression ] "=>" expression
```

```swift
match value {
    nil => "missing",
    1..9 => "digit",
    Some(x) => x,
    x when x > 10 => "large",
    _ => "other"
}
```

## Structs

```text
struct ::= "struct" type-name "{"
             [ field { "," field } ]
           "}"

field ::= [ "mut" ] identifier
          [ ":" type-annotation ] [ "=" expression ] [ "..." ]
```

The field list defines the generated constructor. Fields are read-only by default; `mut` marks an
individual field as writable.

## Enums

```text
enum      ::= "enum" type-name "{" enum-case { "," enum-case } "}"
enum-case ::= identifier [ "(" [ field { "," field } ] ")" ] [ block ]
```

```swift
enum Option { None, Some(value) }
enum Result { Ok(value), Err(error) }
```

## Traits

```text
trait ::= "trait" type-name "{"
            { "func" function-signature separator }
          "}"
```

Trait functions are contracts without bodies.

## Implementations

```text
impl ::= "impl" declared-type
         [ "with" declared-type { "," declared-type } ]
         "{"
           { function | internal-binding }
         "}"
```

An `impl` can provide internal state, an `init` function, methods, properties, and trait
conformance.

```swift
impl Point with Displayable {
    mut cached
    func init(x, y) { this.cached = nil }
    func Describe() => fmt("{0}:{1}", this.x, this.y)
}
```

## Imports and visibility

```text
import ::=
    "import" module-path [ "as" identifier ]
  | "import" identifier "from" module-path
  | "import" "*" "from" module-path

module-path ::= path-part { "/" path-part }
```

Imports are local and are not re-exported. Module declarations are public by default. `private`
may prefix module-level bindings, constants, functions, structs, enums, and traits, but not imports
or `impl` members.

## Exceptions

```text
try-form ::= "try" block
             [ "catch" [ identifier ] block ]
             [ "finally" block ]
```

`throw` raises a value. `Exception<Tag>(...)` creates a tagged Spellkit exception.

## Regions

```text
region ::= '#region' string { statement } '#endregion'
```

Regions are primarily used by the `.kit` test corpus to name independent test cases.

## Current omissions

The grammar does not currently provide:

- general generic type or method syntax;
- class declarations or inheritance;
- automatic string interpolation;
- preprocessor directives such as `#warning`;
- implicit host-object reflection in the Hosting API.
