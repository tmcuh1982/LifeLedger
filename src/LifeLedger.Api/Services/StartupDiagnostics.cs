using System.Net.Sockets;

namespace LifeLedger.Api.Services;

/// <summary>Classifies low-level host startup failures so local users receive an actionable message.</summary>
public static class StartupDiagnostics
{
    /// <summary>Returns whether an exception chain contains the socket error produced by an occupied listening port.</summary>
    public static bool IsAddressAlreadyInUse(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current is SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse }) return true;
        }

        return false;
    }
}
