namespace CryptoScanner.Core.Models;

public sealed record LabParameters(int Id,string Name,string Mutation,decimal MinimumPressure,decimal StopScale)
{
    public const decimal InitialCapital=10000m, Ticket=1000m, Fee=.001m, Slippage=.0005m;
    public const int MaxPositions=5;
    public static IReadOnlyList<LabParameters> Initial => [
        new(1,"Referência","Modelo inicial: score ≥55, pressão ≥50",50,1),
        new(2,"Pressão flexível","Filha da referência: pressão ≥40",40,1),
        new(3,"Pressão exigente","Filha da referência: pressão ≥60",60,1),
        new(4,"Stop mais próximo","Filha da referência: distância do stop ×0,85",50,.85m),
        new(5,"Stop mais distante","Filha da referência: distância do stop ×1,15",50,1.15m)];
}

public sealed record LabOpportunity(string Symbol,string Profile,long GridTimeMs,long QuoteTimeMs,
    decimal? Quote,int HoldingHours,AssetScore Asset,string FeaturesJson)
{
    public long DecisionTimeMs { get; init; }
}
public sealed record LabExit(long AtMs,string Reason,decimal FillPrice,decimal Fraction,decimal Proceeds);

public sealed class LabTrade
{
    public long Id { get; set; }
    public int VariantId { get; set; }
    public string Symbol { get; set; }="";
    public string Profile { get; set; }="";
    public long EntryMs { get; set; }
    public long DeadlineMs { get; set; }
    public decimal EntryFill { get; set; }
    public decimal Quantity { get; set; }
    public decimal Cost { get; set; }=LabParameters.Ticket;
    public decimal FeeRate { get; set; }=LabParameters.Fee;
    public decimal SlippageRate { get; set; }=LabParameters.Slippage;
    public decimal Stop { get; set; }
    public decimal? Tp1 { get; set; }
    public decimal Tp2 { get; set; }
    public decimal? Tp3 { get; set; }
    public bool Tp1Hit { get; set; }
    public bool Tp2Hit { get; set; }
    public decimal Remaining { get; set; }=1;
    public decimal Proceeds { get; set; }
    public decimal LastPrice { get; set; }
    public long LastQuoteMs { get; set; }
    public bool HasObservationGap { get; set; }
    public bool Closed { get; set; }
    public string ExitReason { get; set; }="";
    public decimal NetProfit => Proceeds-Cost;
    public decimal LiquidationValue => Remaining*Quantity*LastPrice*(1-SlippageRate)*(1-FeeRate);
    public DateTime EntryLocal => DateTimeOffset.FromUnixTimeMilliseconds(EntryMs).LocalDateTime;
    public DateTime QuoteLocal => DateTimeOffset.FromUnixTimeMilliseconds(LastQuoteMs).LocalDateTime;
    public string Status => Closed ? ExitReason : "Aberto";
    public decimal? ClosedReturnPercent => Closed ? NetProfit/Cost*100 : null;
}

public static class LabSimulation
{
    public static (LabTrade? Trade,string Reason) TryOpen(LabOpportunity o,LabParameters p,decimal cash,int positions,bool symbolOpen)
    {
        long decision=Math.Max(o.QuoteTimeMs,o.DecisionTimeMs);
        if (o.Quote is not >0 || o.QuoteTimeMs<o.GridTimeMs || o.QuoteTimeMs-o.GridTimeMs>60000 || decision-o.QuoteTimeMs>30000) return(null,"Cotação indisponível ou atrasada");
        if (o.Asset.Score<55) return(null,"Score abaixo de 55");
        if (o.Asset.BuyingPressureScore is null) return(null,"Pressão indisponível");
        if (o.Asset.BuyingPressureScore<p.MinimumPressure) return(null,"Pressão abaixo do limite");
        if (symbolOpen) return(null,"Já existe posição neste ativo");
        if (positions>=LabParameters.MaxPositions) return(null,"Limite de posições");
        if (cash<LabParameters.Ticket) return(null,"Capital indisponível");
        decimal quote=o.Quote.Value,entry=quote*(1+LabParameters.Slippage);
        if (o.Asset.Support<=0 || o.Asset.Support>=quote || o.Asset.Resistance<=entry) return(null,"Stop ou alvo inválido");
        decimal stop=quote-(quote-o.Asset.Support)*p.StopScale;
        if (stop<=0 || stop>=quote) return(null,"Stop da variante inválido");
        if (o.Asset.TakeProfit1.HasValue && (o.Asset.TakeProfit1<=entry || o.Asset.TakeProfit1>=o.Asset.Resistance ||
            !o.Asset.TakeProfit3.HasValue || o.Asset.TakeProfit3<=o.Asset.Resistance)) return(null,"Alvos parciais inválidos");
        if (o.HoldingHours<=0) return(null,"Prazo inválido");
        return(new LabTrade { VariantId=p.Id,Symbol=o.Symbol,Profile=o.Profile,EntryMs=decision,
            DeadlineMs=decision+o.HoldingHours*3600000L,EntryFill=entry,
            Quantity=LabParameters.Ticket/(entry*(1+LabParameters.Fee)),Stop=stop,Tp1=o.Asset.TakeProfit1,
            Tp2=o.Asset.Resistance,Tp3=o.Asset.TakeProfit3,LastPrice=quote,LastQuoteMs=decision },"Entrada aceita");
    }

    public static IReadOnlyList<LabExit> Tick(LabTrade t,decimal price,long at)
    {
        var exits=new List<LabExit>();
        if(t.Closed || price<=0 || at<=t.LastQuoteMs) return exits;
        if(at-t.LastQuoteMs>90000) t.HasObservationGap=true;
        t.LastQuoteMs=at;t.LastPrice=price;
        void Exit(decimal fraction,decimal raw,string reason)
        {
            decimal fill=raw*(1-t.SlippageRate);
            decimal proceeds=t.Quantity*fraction*fill*(1-t.FeeRate);
            t.Remaining-=fraction;t.Proceeds+=proceeds;
            exits.Add(new(at,reason,fill,fraction,proceeds));
            if(t.Remaining==0){t.Closed=true;t.ExitReason=reason;}
        }
        // A stop crossing fills at the observed quote, never at a better, unseen stop price.
        if(price<=t.Stop){Exit(t.Remaining,price,"SL");return exits;}
        if(t.Tp1 is null)
        { if(price>=t.Tp2) Exit(t.Remaining,t.Tp2,"TP"); }
        else
        {
            if(!t.Tp1Hit && price>=t.Tp1.Value){Exit(.4m,t.Tp1.Value,"TP1");t.Tp1Hit=true;}
            if(t.Tp1Hit && !t.Tp2Hit && price>=t.Tp2){Exit(.4m,t.Tp2,"TP2");t.Tp2Hit=true;t.Stop=Math.Max(t.Stop,t.EntryFill);}
            if(t.Tp2Hit && t.Tp3.HasValue && price>=t.Tp3.Value) Exit(t.Remaining,t.Tp3.Value,"TP3");
        }
        if(!t.Closed && at>=t.DeadlineMs) Exit(t.Remaining,price,"Prazo");
        return exits;
    }
}

public sealed record LabVariantReport(string Name,string Mutation,decimal Cash,decimal Equity,decimal Drawdown,
    long Open,long Closed,long Wins,long Rejected,long Gaps)
{
    public decimal NetReturnPercent => (Equity/LabParameters.InitialCapital-1)*100;
    public decimal? WinPercent => Closed==0 ? null : Wins*100m/Closed;
}
public sealed record LabDecisionRow(string Symbol,string Profile,int VariantId,long AtMs,bool Accepted,string Reason,string Details)
{
    public DateTime AtLocal => DateTimeOffset.FromUnixTimeMilliseconds(AtMs).LocalDateTime;
    public string Decision => Accepted?"Aceita":"Rejeitada";
}
public sealed record LabReport(bool Enabled,IReadOnlyList<LabVariantReport> Variants,IReadOnlyList<LabTrade> Trades,long Opportunities,IReadOnlyList<LabDecisionRow> Decisions);
