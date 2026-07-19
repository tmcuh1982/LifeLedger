using System.Net.Sockets;
using LifeLedger.Api.Services;
using Xunit;

namespace LifeLedger.Api.Tests;

/// <summary>Protects the friendly startup diagnosis shown when the local HTTP port is already occupied.</summary>
public sealed class StartupDiagnosticsTests
{
    /// <summary>Recognises the nested socket exception emitted by Kestrel for a duplicate local instance.</summary>
    [Fact]
    public void Address_in_use_is_recognised_through_wrapped_exceptions()
    {
        var exception = new IOException("Kestrel failed", new InvalidOperationException("Binding failed", new SocketException((int)SocketError.AddressAlreadyInUse)));

        Assert.True(StartupDiagnostics.IsAddressAlreadyInUse(exception));
    }

    /// <summary>Does not misclassify unrelated startup failures as a duplicate instance.</summary>
    [Fact]
    public void Unrelated_io_error_is_not_reported_as_a_port_conflict()
    {
        Assert.False(StartupDiagnostics.IsAddressAlreadyInUse(new IOException("Database unavailable")));
    }
}
