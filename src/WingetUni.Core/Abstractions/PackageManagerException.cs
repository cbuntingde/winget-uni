namespace WingetUni.Core.Abstractions;

/// <summary>
/// Raised when a package manager cannot answer a query: it is not installed, it did not
/// respond, or it reported a failure while listing or searching.
/// </summary>
/// <remarks>
/// Queries throw because they have no result object to carry a failure, unlike an operation,
/// which returns an <see cref="Models.OperationResult"/>. An empty result is not a failure and
/// never raises this exception.
/// </remarks>
public sealed class PackageManagerException : Exception
{
    public PackageManagerException(string message)
        : base(message)
    {
    }

    public PackageManagerException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
