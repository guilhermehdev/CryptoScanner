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

    // Espelhos pro lado de baixa (Fase A do lado de venda) — BreakOfStructure/ChangeOfCharacter
    // acima só cobrem rompimento e reversão de ALTA. Retest do nível rompido fica de fora
    // de propósito por enquanto: é rastreamento ao longo do tempo, não cálculo pontual como
    // o resto — tratado como passo separado depois que o resto estiver validado.
    public bool BearishBreakOfStructure { get; set; }

    public bool BearishChangeOfCharacter { get; set; }

    public bool StrongUptrend { get; set; }

    public bool StrongDowntrend { get; set; }

    public int SwingHighCount { get; set; }

    public int SwingLowCount { get; set; }

    // Posição (índice na lista de candles recebida) dos dois últimos topos — pra quem
    // chama poder cruzar com outra série (RSI, por exemplo) no mesmo ponto exato, sem
    // duplicar a lógica de detecção de pivô que já existe aqui. -1 = não disponível
    // (menos de 2 topos encontrados).
    public int LastSwingHighIndex { get; set; } = -1;

    public int PrevSwingHighIndex { get; set; } = -1;
}