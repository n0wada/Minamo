using Minamo.Debug;
using Minamo.Runtime.Types;
using Minamo.Compiler;
using Minamo.Diagnostics;
using System.Linq;
using System.Text;

namespace Minamo.Runtime;

public class MinamoRuntimeException : MinamoException
{
    public MinamoRuntimeException(string message, Exception? innerException) : base(message, innerException) { }

    public MinamoRuntimeException(string message) : base(message) { }
}

public sealed class MinamoCodeException : MinamoRuntimeException
{
    public override string Message => ErrorGenerators.GetErrorDescription(Error);

    public MinamoObject Error { get; }

    public CallStackTrace? CallTrace { get; private set; }

    internal MinamoCodeException(MinamoObject err, CallStackTrace cs, Exception? innerException)
        : base(null!, innerException) => (Error, CallTrace) = (err, cs);

    public MinamoCodeException(MinamoObject err) : base(null!, null) => Error = err;

    public MinamoCodeException(MinamoError errorCode, params object[] args) : base(null!, null) =>
        Error = ErrorGenerators.RuntimeException(errorCode, args);

    public override string ToString()
    {
        var header = Error is MinamoExceptionObject ex
            ? $"{ex.Name}: {Message}"
            : $"Error D{((int)ErrorGenerators.GetErrorCode(Error)).ToString().PadLeft(3, '0')}: {Message}";
        return CallTrace is null ? header : $"{header}\nStack trace:\n{CallTrace}";
    }
}

public enum MinamoError
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
