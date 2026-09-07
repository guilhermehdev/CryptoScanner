namespace CryptoScanner.Core.Models;
public sealed record BacktestPortfolioSummary(decimal InitialCapital,decimal FinalCapital,decimal MaxRealizedDrawdownPercent,int Accepted,int Rejected)
{
    public decimal ReturnPercent => (FinalCapital/InitialCapital-1)*100;
    public static BacktestPortfolioSummary Calculate(IEnumerable<BacktestTradeResult> trades)
    {
        decimal cash=LabParameters.InitialCapital,peak=cash,maxDrop=0;
        var open=new List<BacktestTradeResult>();int accepted=0,rejected=0;
        void Settle(DateTime at)
        {
            foreach(var trade in open.Where(t=>t.ExitTime<=at).OrderBy(t=>t.ExitTime).ThenBy(t=>t.Symbol).ToArray())
            {
                cash+=LabParameters.Ticket*(1+trade.OutcomePercent/100);open.Remove(trade);
                decimal equity=cash+open.Count*LabParameters.Ticket;
                peak=Math.Max(peak,equity);maxDrop=Math.Max(maxDrop,(peak-equity)/peak*100);
            }
        }
        foreach(var trade in trades.OrderBy(t=>t.EntryTime).ThenByDescending(t=>t.Score).ThenBy(t=>t.Symbol,StringComparer.Ordinal))
        {
            Settle(trade.EntryTime);
            if(cash<LabParameters.Ticket||open.Count>=LabParameters.MaxPositions||open.Any(t=>t.Symbol==trade.Symbol)){rejected++;continue;}
            cash-=LabParameters.Ticket;open.Add(trade);accepted++;
        }
        Settle(DateTime.MaxValue);
        return new(LabParameters.InitialCapital,cash,maxDrop,accepted,rejected);
    }
}
