using CryptoScanner.Core.Models;

namespace CryptoScanner.Core.Contracts;

public interface ISignalRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SignalHistory>> GetSignalsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SignalHistory>> GetPendingSignalsAsync(CancellationToken cancellationToken = default);
    Task<bool> SignalExistsWithinWindowAsync(string symbol, string signal, int windowDays, CancellationToken cancellationToken = default);
    Task InsertSignalAsync(SignalSnapshot snapshot, CancellationToken cancellationToken = default);
    Task UpdateSignalResultAsync(int id, decimal outcomePrice, decimal outcomePercent, string exitReason, CancellationToken cancellationToken = default);
    Task<double> GetWinRateAsync(CancellationToken cancellationToken = default);
    Task<double> GetAverageReturnAsync(CancellationToken cancellationToken = default);
}