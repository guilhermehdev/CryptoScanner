using CryptoScanner.Core.Models;

namespace CryptoScanner.Core.Contracts;

public interface ISignalRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SignalHistory>> GetSignalsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SignalHistory>> GetPendingSignalsAsync(CancellationToken cancellationToken = default);
    Task<bool> SignalExistsWithinWindowAsync(string symbol, string signal, string profile, int windowDays, CancellationToken cancellationToken = default);
    Task<bool> TryInsertSignalAsync(SignalSnapshot snapshot, int windowDays, CancellationToken cancellationToken = default);
    Task UpdateExecutionAsync(int id, string expectedJson, LabTrade trade, CancellationToken cancellationToken = default);
    Task SaveScanRunAsync(string profile, FilterDiagnostics diagnostics, CancellationToken cancellationToken = default);
    Task UpdateSignalResultAsync(int id, decimal outcomePrice, decimal outcomePercent, string exitReason, CancellationToken cancellationToken = default);
    Task<double> GetWinRateAsync(CancellationToken cancellationToken = default);
    Task<double> GetAverageReturnAsync(CancellationToken cancellationToken = default);
}
