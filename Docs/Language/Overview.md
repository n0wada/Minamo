# Spellkit language overview

Spellkit is a compact dynamic language for embedded .NET scripting. This page is a map of the
language rather than a complete specification. See the [recipes](Recipes.md) and the
[language tests](../../Spellkit.UnitTests/Tests) for more examples.

## Comments

Line comments start with `//`. Block comments use `/* ... */` and may be nested.

```swift
// One line

/*
   A block comment
   /* with a nested comment */
*/
```

## Literals

```swift
nil
true
false
42
3.14159
1.2E+10
'A'
"Spellkit"
[1, 2, 3]
(1, "two", true)
(name: "Ada", age: 33)
[name: "Ada", age: 33]
```

`Integer` values use signed 64-bit storage. `Float` values use .NET `double` precision.

## Strings

Strings use double quotes. Characters use single quotes. Triple-quoted strings span multiple
lines. Formatting is explicit through `fmt`.

```swift
let name = "Spellkit"
let greeting = fmt("Hello, {0}", name)
let text = """
Line one
Line two
"""
```

## Variables and constants

`let` creates an immutable binding, `mut` creates a mutable binding, and `const` declares constants.
`use` disposes its value when the current scope exits.

```swift
let name = "Ada"
mut score = 0
const MaxAttempts = 3
use file = File.Create("output.txt")

score += 10
```

Optional type annotations are descriptive metadata rather than a separate static type system.
They are not consistently enforced at compile time or runtime.

```swift
let count: Integer = 3
mut value: String? = nil
let result: Result<String> = Ok("ready")
```

`String?` is shorthand for `String | Nil`. Parameterized hints retain their arguments for
tooling and documentation, while execution currently uses only the outer type name.

## Operators

Spellkit provides arithmetic, comparison, logical, assignment, range, conditional, and
nil-coalescing operators.

```swift
let total = price * count
let ready = enabled && count > 0
let fallback = value ?? "unknown"
let sign = number >= 0 ? 1 : -1
let values = [1..10]
```

## Functions

Functions may use a block or expression body. Parameters support annotations, defaults, named
arguments, and variadic capture.

```swift
func add(x: Integer, y: Integer): Integer {
    x + y
}

func greet(name = "world") =>
    fmt("Hello, {0}", name)

func collect(values...) => values

add(y: 2, x: 1)
```

Functions are first-class values.

```swift
let double = x => x * 2
let transform = (value, operation) => operation(value)
```

## Conditional flow

`if` can be used as a statement or expression. Spellkit also provides `guard` and the conditional
operator.

```swift
let description = if score > 0 {
    "positive"
} else {
    "zero or negative"
}

guard connection.IsOpen {
    return nil
}
```

## Loops

Use `while`, `do ... while`, or `for`. Loops support `break`, `continue`, filters, and `else`.

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

`break` may provide the value of a loop expression.

## Interactive selects

`select` defines a host-driven interaction with choices, suitable for menus, dialogue, and
quests. A select expression produces a reusable factory; each invocation creates a new
interaction instance.

```swift
let shop = select {
    choose "browse" => {
        print("You browse the shelves.")
    }

    choose "leave" => exit "closed"
}
```

Save the script as `shop.kit`, then start its named select from the console:

```powershell
spell.exe shop.kit --do shop
```

The console presents the choices after it executes the file. Selecting `"browse"` runs its choice
body and republishes the same interaction; selecting `"leave"` exits it. Hidden host events may be
declared with `on` and delivered from C# with `Send`; `on empty` handles an interaction with no
available choices or host events. Use named `state` declarations and `goto` when the interaction
needs explicit state transitions. Named selects can also be opened from C#.

See [Interactive selects](../Developers/InteractiveSelect.md) for basic host integration and
[Advanced interactive selects](../Developers/InteractiveSelectAdvanced.md) for factory lifetime,
nesting, aliases, and the C# session API.

## Collections

The main collection forms are arrays, tuples, labeled tuples, dictionaries, sets, ranges, and
iterators.

```swift
let array = [1, 2, 3]
let tuple = (1, "two")
let record = (name: "Ada", age: 33)
let dictionary = ["one": 1, "two": 2]
let doubled = [x * 2 for x in array]
```

Collections support indexing and common conversion and iteration helpers.

## Pattern matching

`match` supports literals, ranges, sequences, constructors, type guards, and combined patterns.

```swift
let result = match value {
    nil => "missing",
    0 => "zero",
    1..9 => "digit",
    Some(x) => fmt("value: {0}", x),
    x when x is String => x,
    _ => "other"
}
```

The `is` operator supports literal, range, type, negated, tuple, array, and constructor patterns.
Separate `is` conditions can be joined with `||`.

```swift
if value is nil
    || value is true
    || value is 0
    || value is 1..9
    || value is String
    || value is not Dictionary
    || value is (_, _)
    || value is [_, _, _]
    || value is Ok(_) {
    print("recognized pattern")
}
```

## Types

`struct` declares a nominal data type. Fields are read-only by default; prefix a field with `mut`
when it must be writable. Type annotations follow the field name. `enum` declares alternatives
that may carry values using the same field syntax.

```swift
struct Point { mut x: Float, y: Float }
enum Result { Ok(value: String), Err(error: String) }

let point = Point(10.0, 20.0)
let result = Ok(42)
```

## Traits and implementations

Traits declare required behavior. `impl` supplies internal state, initializers, methods, and trait
conformance.

```swift
trait Displayable {
    func Describe()
}

impl Point with Displayable {
    func Describe() => fmt("{0}:{1}", this.x, this.y)
}
```

A qualified function adds behavior to an existing type.

```swift
func Integer.Double() => this * 2
```

## Properties and operators

Custom types can define methods, getters, setters, indexers, and operators.

```swift
func get Point.Length() =>
    Sqrt(this.x * this.x + this.y * this.y)

func Point + (other) =>
    Point(this.x + other.x, this.y + other.y)
```

## Iterators

A function containing `yield` produces an iterator. `yield break` ends it early.

```swift
func sequence() {
    yield 1
    yield 2
    yield 3
}

for value in sequence() {
    print(value)
}
```

## Modules

Modules are imported by path or registered by a C# host.

```swift
import refs/math
import refs/math as mathematics
import square from refs/math
import * from text
```

Module declarations are public by default. Use `private` for declarations that must remain inside
the module. Imports are local and are not re-exported.

## Exceptions

```swift
func loadUser(name) {
    if name == "" {
        throw Exception<UserError>("A user name is required.")
    }

    findUser(name)
}

try {
    loadUser("")
} catch error {
    print(fmt("[{0}] {1}", error.Name, error.Message))
}
```

`Exception<UserError>` creates an exception with an application-defined tag. `catch` receives the
exception object; its `Name` is the `UserError` tag and its `Message` contains the supplied message.
Both can be written to a console or forwarded to a host logging command. A `finally` block may be
added when cleanup must run after either success or failure.

## Embedding in .NET

The application-facing API lives in `Spellkit.Hosting`. Reference `Spellkit.dll` for the Hosting
API and runtime, and reference `Spellkit.Generators.dll` as an analyzer to expose attributed C#
methods as commands.

```csharp
using Spellkit.Hosting;

[SpellkitModule("app")]
public sealed class AppCommands
{
    [SpellkitCommand("greet")]
    public string Greet(string name) => $"Hello, {name}!";
}

var host = new SpellkitHost();
host.AddModule(new AppCommands());

using var instance = host.CreateInstance();
var result = await instance.ExecuteFileAsync("hello.kit");

if (!result.Success)
    Console.Error.WriteLine(result.Failure?.Message);
```

The `hello.kit` file imports the generated module and calls its command:

```swift
import app
app.greet("Spellkit")
```

Instances are incremental, so definitions from successful executions remain available to later
calls. `ExecuteAsync` and `ExecuteFileAsync` provide asynchronous host-call surfaces. Spellkit has
no language-level `async` or `await` syntax: an ordinary call to an asynchronous host command
suspends the VM, and the C# hosting surface resumes it when the returned `Task` or `ValueTask`
completes. The selected entry file can always be executed, while additional file imports remain
disabled unless the host supplies an explicit `FileLookup`.

When the same script should be shared by many actors, compile it once and create separate
instances:

```csharp
var source = """
    import app
    app.greet("Spellkit")
    """;

var program = host.Compile(source).GetValueOrThrow();

using var first = host.CreateInstance(program);
using var second = host.CreateInstance(program);

await first.ExecuteAsync();
await second.ExecuteAsync();
```

The `SpellkitProgram` holds the compiled code; each `SpellkitInstance` keeps its own environment
and mutable state. C# can expose instance-specific names through that environment:

```csharp
var env = new SpellkitEnvironment(game)
    .Expose("self", player)
    .Expose("world", world);

using var actor = host.CreateInstance(program, env);
```

Those names are resolved as a fallback after locals, imports, and built-ins, so the same compiled
program can run with different host-provided views.

For actor-style scripts, the host object itself can be hidden with `ExposeHostObject = false`.
Then `host` is not declared in the script, and only names explicitly exposed through the
`SpellkitEnvironment` are visible.

## Host integration

A hosted instance can expose deliberately selected modules, state, signals, and resource
handles through the global `host` object.

```swift
import scene
let player = scene.Find("player")
host.State["selected"] = "player"
host.Signals.On("player.hit", damage => print(damage))
```

See the [Hosting API guide](../Developers/HostingGuide.md) for capabilities, execution limits, resource lifetime,
telemetry, and C# integration.

## Next steps

- [Detailed grammar reference](../Reference/Grammar.md)
- [Language recipes](Recipes.md)
- [Interactive selects](../Developers/InteractiveSelect.md)
- [Advanced interactive selects](../Developers/InteractiveSelectAdvanced.md)
- [Hosting API](../Developers/HostingGuide.md)
- [Compatibility](../Operations/Compatibility.md)
- [Runnable Station Console example](../../Examples/StationConsole/README.md)
- [Interactive Quest Console example](../../Examples/QuestConsole/README.md)
