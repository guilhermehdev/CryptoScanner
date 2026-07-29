namespace CryptoScanner.Core.Models;

public sealed record NewSignalAlert(string Symbol, string Signal, decimal Score, decimal Price, string Profile);