using System.Text.Json;
using CryptoScanner.Core.Configuration;
using CryptoScanner.Core.Contracts;
using CryptoScanner.Core.Models;

namespace CryptoScanner.Application.Services;

public sealed class StrategyLabService(IStrategyLabRepository repository,IMarketDataService market)
{
    private const int EvaluationQuoteConcurrency = 12;
    private readonly SemaphoreSlim _observe=new(1,1),_evaluate=new(1,1);
    public async Task ObserveGridAsync(IReadOnlyList<AssetScore> assets,ScanProfile profile,CancellationToken token=default)
    {
        if(!await _observe.WaitAsync(0,token))return;
        try
        {
            long captured=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            // Freeze mutable grid prices/indicators before awaiting network requests.
            var frozen=assets.Select(a=>JsonSerializer.Serialize(a)).ToArray();
            var observed=await repository.ObservedSymbolsAsync(profile.Name,captured/300000,token);
            using var throttle=new SemaphoreSlim(4);
            var requests=frozen.Select(async json=>
            {
                var asset=JsonSerializer.Deserialize<AssetScore>(json)!;
                if(observed.Contains(asset.Symbol))return null;
                await throttle.WaitAsync(token);
                try
                {
                    decimal? price=await QuoteAsync(asset.Symbol,token);
                    return new LabOpportunity(asset.Symbol.ToUpperInvariant(),profile.Name,captured,
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),price,profile.EvaluationHours,asset,json);
                }
                finally{throttle.Release();}
            });
            // Keep grid order as the common capital-allocation priority for all variants.
            foreach(var opportunity in await Task.WhenAll(requests))
                if(opportunity is not null)await repository.ObserveAsync(opportunity with
                    {DecisionTimeMs=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},token);
        }
        finally{_observe.Release();}
    }

    public async Task EvaluateAsync(CancellationToken token=default)
    {
        if(!await _evaluate.WaitAsync(0,token))return;
        try
        {
            var symbols=(await repository.OpenSymbolsAsync(token))
                .Where(symbol=>!string.IsNullOrWhiteSpace(symbol))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if(symbols.Length==0)return;

            // Query open positions concurrently. A sequential pass through dozens of
            // shadow trades can exceed the 90-second observation window by itself.
            using var throttle=new SemaphoreSlim(EvaluationQuoteConcurrency);
            var quotes=await Task.WhenAll(symbols.Select(async symbol=>
            {
                await throttle.WaitAsync(token);
                try{return (Symbol:symbol,Price:await QuoteAsync(symbol,token));}
                finally{throttle.Release();}
            }));
            var prices=quotes.Where(quote=>quote.Price is >0).ToDictionary(
                quote=>quote.Symbol,quote=>quote.Price!.Value,StringComparer.OrdinalIgnoreCase);
            if(prices.Count>0)
                // One timestamp and one transaction make the cycle a coherent price snapshot.
                await repository.TickAsync(prices,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),token);
        }
        finally{_evaluate.Release();}
    }

    private async Task<decimal?> QuoteAsync(string symbol,CancellationToken token)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try{return await market.GetCurrentPriceAsync(symbol,timeout.Token);}
        catch(Exception) when(!token.IsCancellationRequested){return null;}
    }
}
