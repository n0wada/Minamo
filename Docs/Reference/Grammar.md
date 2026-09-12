# Minamo grammar reference

This document describes the grammar accepted by the current handwritten parser. The parser and
the passing files under [`Minamo.UnitTests/Tests`](../../Minamo.UnitTests/Tests) remain
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

Broad categories such as `statement`, `expression`, and `primary` are cross-references to the
corresponding sections rather than a second, fully expanded expression grammar. Lexical categories
and context-sensitive boundaries such as `separator` and `same-line-expression` are explained in
the adjacent prose.

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
qualified-name ::= identifier { "." identifier }
```

Reserved words include:

```text
as break catch continue do else false for from func get if import in is
let match mut nil not or private return set static throw true try type
use when while with yield
```

`const`, `struct`, `enum`, `trait`, `impl`, `guard`, `finally`, `select`, `prop`, `case`, `on`,
`desc`, `goto`, and `exit` are
contextual keywords.

## Numeric literals

```text
integer  ::= digits | ("0x" | "0X") hex-digits
fraction ::= digits "." digits | "." digits
exponent ::= ("e" | "E") [ "+" | "-" ] digits
float    ::= fraction [ exponent ] [ float-suffix ]
           | digits exponent [ float-suffix ]
           | digits float-suffix
float-suffix ::= "f" | "F"
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
literal       ::= "nil" | "true" | "false" | integer | float
                | string | multiline-string | character
argument      ::= expression | label
array-element ::= expression | label
label         ::= [ "let" | "mut" ] (identifier | string) ":" expression
tuple         ::= "(" [ argument { "," argument } [ "," ] ] ")"
array         ::= "[" [ array-element { "," array-element } [ "," ] ] "]"
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

Annotations are descriptive metadata, not a separate static type system. Minamo does not
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
    [ "get" | "set" ] function-name parameter-list [ ":" type-annotation ]
  | [ "get" | "set" ] type-name indexer-parameter-list [ ":" type-annotation ]
  | type-name "as" type-name
  | identifier function-operator parameter-list [ ":" type-annotation ]

function-name ::= identifier | type-name "." identifier
parameter-list ::= "(" [ parameter { "," parameter } [ "," ] ] ")"
indexer-parameter-list ::= "[" [ parameter { "," parameter } [ "," ] ] "]"

function-operator ::=
    "+" | "-" | "*" | "/" | "%" | "!"
  | "==" | "!=" | "<" | ">" | "<=" | ">=" | "<<" | ">>"

parameter ::=
    [ type-annotation ] identifier
    [ ":" type-annotation ]
    [ "=" expression ]
    [ "..." ]

arrow-body ::=
    binding | assignment | expression
  | return-statement | yield-statement | break-statement | "continue" | throw-statement
  | if-form | guard-form | while-loop | do-while-loop | for-loop | match
```

```swift
func add(x: Integer, y: Integer): Integer { x + y }
func greet(name = "world") => fmt("Hello, {0}", name)
func collect(values...) => values
```

The final expression of a block is its value.

## Interactive selects

An interactive select publishes properties and cases to the host and executes the selected case
action. A host opens it through `MinamoInstance.OpenSelectAsync`. The C# API exposes language cases
as `MinamoChoice` objects in `MinamoSelect.Choices`. See
[Interactive selects](../Developers/InteractiveSelect.md) for the basic C# protocol and
[Advanced interactive selects](../Developers/InteractiveSelectAdvanced.md) for asynchronous hosts
and nested interactions.

```text
select-declaration
    ::= "select" [ identifier ] "{" [ select-description ] { select-local }
        { select-property | case-declaration | event-declaration } "}"

select-description
    ::= "desc" string-key-dictionary-literal

string-key-dictionary-literal
    ::= "[" [ string ":" expression { "," string ":" expression } ] "]"

select-local
    ::= ( "let" | "mut" ) pattern "=" expression

select-property
    ::= "prop" ( identifier | string ) [ select-metadata ] "=>" expression

case-declaration
    ::= "case" string [ select-parameters ]
        [ "when" expression ] [ select-metadata ]
        "=>" select-action-body

select-metadata
    ::= string-key-dictionary-literal

event-declaration
    ::= "on" string [ select-parameters ]
        "=>" select-action-body

select-parameters
    ::= "(" [ select-parameter { "," select-parameter } [ "," ] ] ")"

select-parameter
    ::= identifier [ ":" type-annotation ]

select-action-body
    ::= block | select-control-statement

select-control-statement
    ::= goto-statement | select-return-statement | exit-statement

goto-statement
    ::= "goto" same-line-expression

select-return-statement
    ::= "return"

exit-statement
    ::= "exit" [ same-line-expression ]

```

Named select declarations are permitted only at global (module) scope. Select locals are created
for each interaction instance and must appear before properties, cases, or events. A `prop`
declaration publishes a read-only value to the host. Its name may be an identifier or string.
Properties and their optional metadata dictionaries are reevaluated for every successful
publication. After an ordinary action finishes, the current select republishes its properties and
choices. `goto expression` evaluates an expression
that must produce a select factory, creates a fresh instance from it, pushes the current instance
onto the interaction's navigation stack, and publishes the new instance. `return` completes the
current instance with `nil` and restores the preceding instance. At the root it completes the
interaction with `nil`. `exit` completes the whole interaction, discarding every stacked instance,
and may supply the interaction's result. There is no `back` keyword.
Property names, case IDs, and event names are unique within their respective channels. Cases and
events receive either no
argument, one value, or a tuple whose
elements bind to their parameters. `case` declarations are visible through `Choices`; `on`
declarations are hidden and delivered through `MinamoSelect.SendAsync`. `when` controls whether a
case is currently available. A case's optional metadata dictionary is evaluated only when that case
is available. Select metadata is free-form UI guidance; Minamo requires string keys but does not
assign meanings to them.
When no choice is available and the select has no host events, the interaction completes with
`nil`; when the current select was reached by `goto`, this behaves like `return` and restores its
caller. Select-local values are available to all actions in their select instance and remain intact
while that instance is on the navigation stack.
Selects are opened and driven by the host; scripts cannot invoke them with `do expression`.
A case action may call `request(kind, payload?)` to yield an application-defined input request to
the host. The host responds through `MinamoSelect.RespondAsync`, and the response becomes the return
value of `request`. Requests are not valid in host events, guards, or descriptions.
The select control statements are valid in case and event action bodies. They do not change the
meaning of `return` inside a function declared within an action: there it remains an ordinary
function return and may carry a value. A select-level `return` cannot carry a value.
`desc` is an optional free-form select description. It must be a dictionary literal whose keys are
strings, and is evaluated once when an interaction instance opens. A host may use it as explanatory
text, an AI prompt, or broad layout guidance. Metadata dictionaries on properties and cases provide
more local UI hints and are reevaluated with the published state.
The `request(...)` and `alias(...)` calls are ordinary built-in function calls rather than select
syntax.

```swift
select player {
    mut playing = false

    prop playing => playing

    case "play" when !playing && music.HasSelectedTrack() [
        "text": "Play",
        "control": "button"
    ] => {
        music.Play()
        playing = true
    }

    case "stop" when playing => {
        music.Stop()
        playing = false
    }

    case "exit" => exit "done"
}

alias(player, "music.player")
```

Select navigation uses factory expressions rather than names encoded as strings:

```swift
select details {
    case "close" => return
}

select menu {
    case "details" => goto details
    case "quit" => exit "done"
}
```

## Lambdas

```text
lambda ::=
    identifier "=>" expression
  | parameter-list "=>" expression
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
    | "(" [ argument { "," argument } [ "," ] ] ")"
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

assignment ::= expression assignment-operator expression
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
same-line-expression ::= expression
```

No line break may occur between `return`, `break`, `throw`, or `exit` and its optional expression.
The required expression following a select `goto` must also start on the same line.
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
  | "(" pattern ")"
  | tuple-pattern
  | "[" [ range-pattern { "," range-pattern } [ "," ] ] "]"
  | constructor-pattern

tuple-pattern ::=
    "(" pattern "," [ pattern { "," pattern } [ "," ] ] ")"

constructor-pattern ::=
    identifier [ "." identifier [ "." identifier ] ]
    "(" [ pattern { "," pattern } [ "," ] ] ")"
```

Lowercase names bind values. Uppercase bare names denote types or nullary constructors.

## Match

```text
match ::= "match" expression "{"
            [ match-entry { "," match-entry } [ "," ] ]
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
struct ::= "struct" identifier "{"
             [ field { "," field } [ "," ] ]
           "}"

field ::= [ "mut" ] identifier
          [ ":" type-annotation ] [ "=" expression ] [ "..." ]
```

The field list defines the generated constructor. Fields are read-only by default; `mut` marks an
individual field as writable.

## Enums

```text
enum      ::= "enum" identifier "{" [ enum-case { "," enum-case } [ "," ] ] "}"
enum-case ::= identifier [ "(" [ field { "," field } [ "," ] ] ")" ] [ block ]
```

```swift
enum Option { None, Some(value) }
enum Result { Ok(value), Err(error) }
```

## Traits

```text
trait ::= "trait" identifier "{"
            { "func" function-signature separator }
          "}"
```

Trait functions are contracts without bodies.

## Implementations

```text
impl ::= "impl" identifier
         [ "with" type-name { "," type-name } ]
         "{"
           { function | impl-binding }
         "}"

impl-binding ::= ( "let" | "mut" ) identifier
                 [ ":" type-annotation ] [ "=" expression ]
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
    "import" module-path [ "as" import-name ]
  | "import" import-name "from" module-path
  | "import" "*" "from" module-path

module-path ::= [ "./" ] import-name { "/" import-name }
import-name ::= identifier | string
```

Imports are local and are not re-exported. Module declarations are public by default. `private`
may prefix module-level bindings, constants, functions, structs, enums, and traits, but not imports
or `impl` members. `./` explicitly marks an import as relative; it uses the same lookup order as
the equivalent path without the prefix.

## Exceptions

```text
try-form ::= "try" block
             [ "catch" [ identifier ] block ]
             [ "finally" block ]
```

`throw` raises a value. `Exception<Tag>(...)` creates a tagged Minamo exception.

## Regions

```text
region ::= '#region' (identifier | string) { statement } '#endregion'
```

Regions are primarily used by the `.nami` test corpus to name independent test cases.

## Current omissions

The grammar does not currently provide:

- general generic type or method syntax;
- class declarations or inheritance;
- automatic string interpolation;
- preprocessor directives such as `#warning`;
- implicit host-object reflection in the Hosting API.
