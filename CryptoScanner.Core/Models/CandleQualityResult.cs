using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoScanner.Core.Models;

public class CandleQualityResult
{
    public decimal BullPower { get; set; }

    public decimal BearPower { get; set; }

    public decimal UpperWickRatio { get; set; }

    public decimal LowerWickRatio { get; set; }

    public decimal BodyRatio { get; set; }

    public bool StrongBullish { get; set; }

    public bool StrongBearish { get; set; }

    public bool BuyerRejection { get; set; }

    public bool SellerRejection { get; set; }

    public int Score { get; set; }
}
