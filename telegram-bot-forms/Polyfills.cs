#if NETSTANDARD2_1

// Records with init-only setters need IsExternalInit on targets older than .NET 5.
namespace System.Runtime.CompilerServices;

internal static class IsExternalInit;

#endif
