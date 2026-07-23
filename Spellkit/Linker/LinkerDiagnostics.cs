namespace Spellkit.Linker;

public enum LinkerError
{
    None,

    ModuleNotFound = 400,

    UnableReadModule = 401,

    ChecksumValidationFailed = 408,

    InvalidForeignModule = 410,

    CircularModuleReference = 411
}

public enum LinkerWarning
{
    NewerSourceFile = 500
}
