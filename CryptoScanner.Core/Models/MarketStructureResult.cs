using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoScanner.Core.Models;

public class MarketStructureResult
{
    public int Score { get; set; }

    public bool HigherHigh { get; set; }

    public bool HigherLow { get; set; }

    public bool LowerHigh { get; set; }

    public bool LowerLow { get; set; }

    public bool Uptrend { get; set; }

    public bool Downtrend { get; set; }

    public bool Sideways { get; set; }
    public bool BreakOfStructure { get; set; }

    public bool ChangeOfCharacter { get; set; }

    public bool StrongUptrend { get; set; }

    public bool StrongDowntrend { get; set; }

    public int SwingHighCount { get; set; }

    public int SwingLowCount { get; set; }
}
