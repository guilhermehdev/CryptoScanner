using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoScanner.Exchange.Models;

public class BinanceSymbol
{
    public string Symbol { get; set; } = "";

    public string Status { get; set; } = "";

    public string QuoteAsset { get; set; } = "";
}
