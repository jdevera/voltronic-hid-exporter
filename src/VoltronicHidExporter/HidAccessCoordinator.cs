using System.Text;

namespace VoltronicHidExporter;

public sealed class HidAccessCoordinator(ExporterOptions options)
{
    private readonly string _mutexName = BuildMutexName(options);

    public IDisposable Acquire()
    {
        Mutex mutex;
        try
        {
            mutex = new Mutex(false, _mutexName);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new IOException(
                "The UPS HID interface is already reserved by another exporter process.",
                exception);
        }

        try
        {
            var acquired = false;
            try
            {
                acquired = mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                throw new IOException(
                    "The UPS HID interface is already in use by another exporter process.");
            }

            return new MutexLease(mutex);
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    private static string BuildMutexName(ExporterOptions options)
    {
        var interfaceToken = new StringBuilder(options.InterfaceToken.Length);
        foreach (var character in options.InterfaceToken)
        {
            interfaceToken.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        return $"Global\\VoltronicHidExporter.Hid.{options.VendorId:X4}.{options.ProductId:X4}.{interfaceToken}";
    }

    private sealed class MutexLease(Mutex mutex) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            mutex.ReleaseMutex();
            mutex.Dispose();
            _disposed = true;
        }
    }
}
