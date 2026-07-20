using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using CryptoScanner.Core.Models;

namespace CryptoScanner.UI.Services;

public class SignalHistoryService
{
    private readonly string _file =
        Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Data",
            "signals.json");

    public async Task<List<SignalHistory>> LoadAsync()
    {
        if (!File.Exists(_file))
            return new();

        string json =
            await File.ReadAllTextAsync(_file);

        return JsonSerializer.Deserialize<List<SignalHistory>>(json)
               ?? new();
    }

    public async Task SaveAsync(
     List<SignalHistory> history)
    {
        string? directory =
            Path.GetDirectoryName(_file);

        if (!Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        string json =
            JsonSerializer.Serialize(
                history,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });

        await File.WriteAllTextAsync(
            _file,
            json);
    }
}