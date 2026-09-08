namespace VoltronicHidExporter;

public sealed class ExporterState
{
    private readonly object _gate = new();
    private PollSnapshot? _lastSnapshot;
    private bool _up;
    private string? _lastError;
    private long _pollErrors;
    private long _utilityLosses;
    private DateTimeOffset? _lastUtilityLoss;
    private DateTimeOffset? _currentOutageStarted;

    public UtilityTransition RecordSuccess(PollSnapshot snapshot)
    {
        lock (_gate)
        {
            var wasOnBattery = _lastSnapshot?.Telemetry.OnBattery;
            _lastSnapshot = snapshot;
            _up = true;
            _lastError = null;

            if (snapshot.Telemetry.OnBattery && wasOnBattery is not true)
            {
                _utilityLosses++;
                _lastUtilityLoss = snapshot.Timestamp;
                _currentOutageStarted = snapshot.Timestamp;
                return UtilityTransition.UtilityLost;
            }

            if (!snapshot.Telemetry.OnBattery && wasOnBattery is true)
            {
                _currentOutageStarted = null;
                return UtilityTransition.UtilityRestored;
            }

            return UtilityTransition.None;
        }
    }

    public void RecordFailure(string error)
    {
        lock (_gate)
        {
            _up = false;
            _lastError = error;
            _pollErrors++;
        }
    }

    public ExporterStateView Read()
    {
        lock (_gate)
        {
            return new ExporterStateView(
                _lastSnapshot,
                _up,
                _lastError,
                _pollErrors,
                _utilityLosses,
                _lastUtilityLoss,
                _currentOutageStarted);
        }
    }
}

public sealed record ExporterStateView(
    PollSnapshot? LastSnapshot,
    bool Up,
    string? LastError,
    long PollErrors,
    long UtilityLosses,
    DateTimeOffset? LastUtilityLoss,
    DateTimeOffset? CurrentOutageStarted);
