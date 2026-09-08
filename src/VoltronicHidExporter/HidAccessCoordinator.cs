using System.Text;

namespace VoltronicHidExporter;

public sealed class HidAccessCoordinator(ExporterOptions options)
{
    private readonly string _semaphoreName = BuildSemaphoreName(options);

    public IDisposable Acquire()
    {
        Semaphore semaphore;
        try
        {
            semaphore = new Semaphore(1, 1, _semaphoreName);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new IOException(
                "The UPS HID interface is already reserved by another exporter process.",
                exception);
        }

        try
        {
            if (!semaphore.WaitOne(0))
            {
                throw new IOException(
                    "The UPS HID interface is already in use by another exporter process.");
            }

            return new SemaphoreLease(semaphore);
        }
        catch
        {
            semaphore.Dispose();
            throw;
        }
    }

    private static string BuildSemaphoreName(ExporterOptions options)
    {
        var interfaceToken = new StringBuilder(options.InterfaceToken.Length);
        foreach (var character in options.InterfaceToken)
        {
            interfaceToken.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        return $"Global\\VoltronicHidExporter.Hid.{options.VendorId:X4}.{options.ProductId:X4}.{interfaceToken}";
    }

    private sealed class SemaphoreLease(Semaphore semaphore) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            semaphore.Release();
            semaphore.Dispose();
            _disposed = true;
        }
    }
}
