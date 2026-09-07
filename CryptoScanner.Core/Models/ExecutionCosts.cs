using CryptoScanner.Core.Configuration;
namespace CryptoScanner.Core.Models;
public static class ExecutionCosts
{
    // Same 0.10% fee and 0.05% adverse slippage assumptions as the laboratory.
    public static decimal NetReturn(decimal grossPercent,TradeDirection direction=TradeDirection.Long)
    {
        decimal exitRatio=direction==TradeDirection.Long?1+grossPercent/100:1-grossPercent/100;
        decimal entry=direction==TradeDirection.Long?1+LabParameters.Slippage:1-LabParameters.Slippage;
        decimal exit=exitRatio*(direction==TradeDirection.Long?1-LabParameters.Slippage:1+LabParameters.Slippage);
        decimal pnl=direction==TradeDirection.Long?exit-entry:entry-exit;
        return (pnl-LabParameters.Fee*(entry+exit))/(entry*(1+LabParameters.Fee))*100;
    }
}
