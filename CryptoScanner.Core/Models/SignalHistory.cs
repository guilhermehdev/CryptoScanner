using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoScanner.Core.Models
{
    public class SignalHistory
    {
        public DateTime Timestamp { get; set; }

        public string Symbol { get; set; } = "";

        public decimal Price { get; set; }

        public decimal FinalScore { get; set; }

        public string Signal { get; set; } = "";

        public decimal? OutcomePrice { get; set; }

        public decimal? OutcomePercent { get; set; }

        public bool Evaluated { get; set; }
    }
}
