using Spellkit.Debug;
using Spellkit.Runtime.Types;
using Spellkit.Compiler;
using Spellkit.Diagnostics;
using System.Linq;
using System.Text;

namespace Spellkit.Runtime;

public class SpellkitRuntimeException : SpellkitException
{
    public SpellkitRuntimeException(string message, Exception? innerException) : base(message, innerException) { }

    public SpellkitRuntimeException(string message) : base(message) { }
}

public sealed class SpellkitCodeException : SpellkitRuntimeException
{
    public override string Message => ErrorGenerators.GetErrorDescription(Error);

    public SpellkitObject Error { get; }

    public CallStackTrace? CallTrace { get; private set; }

    internal SpellkitCodeException(SpellkitObject err, CallStackTrace cs, Exception? innerException)
        : base(null!, innerException) => (Error, CallTrace) = (err, cs);

    public SpellkitCodeException(SpellkitObject err) : base(null!, null) => Error = err;

    public SpellkitCodeException(SpellkitError errorCode, params object[] args) : base(null!, null) =>
        Error = ErrorGenerators.RuntimeException(errorCode, args);

    public override string ToString()
    {
        var header = Error is SpellkitExceptionObject ex
            ? $"{ex.Name}: {Message}"
            : $"Error D{((int)ErrorGenerators.GetErrorCode(Error)).ToString().PadLeft(3, '0')}: {Message}";
        return CallTrace is null ? header : $"{header}\nStack trace:\n{CallTrace}";
    }
}

public enum SpellkitError
{
    None,

    UnexpectedError = 601,

    MultipleValuesForArgument = 602,

    ExternalFunctionFailure = 603,

    OperationNotSupported = 604,

    IndexOutOfRange = 605,

    IndexReadOnly = 606,

    DivideByZero = 607,

    TooManyArguments = 608,

    InvalidType = 609,

    PrivateAccess = 610,

    AssertionFailed = 611,

    RequiredArgumentMissing = 612,

    ArgumentNotFound = 613,

    InvalidOperation = 614,

    MatchFailed = 615,

    CollectionModified = 616,

    PrivateNameAccess = 617,

    KeyNotFound = 618,

    KeyAlreadyPresent = 619,

    InvalidOverload = 620,

    InvalidValue = 621,

    InvalidCast = 622,

    ValueMissing = 623,

    Failure = 624,

    Timeout = 625,

    ParsingFailed = 626,

    Overflow = 627,

    MethodNotFound = 628,

    TypeClosed = 629,

    NotImplemented = 630,

    ConstructorFailed = 631,

    IOFailed = 632,

    OverloadProhibited = 633
}
