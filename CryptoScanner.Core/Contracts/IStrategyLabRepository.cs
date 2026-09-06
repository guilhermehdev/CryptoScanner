using CryptoScanner.Core.Models;
namespace CryptoScanner.Core.Contracts;
public interface IStrategyLabRepository
{
    Task<IReadOnlySet<string>> ObservedSymbolsAsync(string profile,long bucket,CancellationToken token=default);
    Task ObserveAsync(LabOpportunity opportunity,CancellationToken token=default);
    Task<IReadOnlyList<string>> OpenSymbolsAsync(CancellationToken token=default);
    Task TickAsync(IReadOnlyDictionary<string,decimal> prices,long at,CancellationToken token=default);
    Task<LabReport> ReportAsync(CancellationToken token=default);
    Task SetEnabledAsync(bool enabled,CancellationToken token=default);
}
