namespace CryptoScanner.Core.Configuration;

public enum RiskCalculationMode
{
    SwingBased,
    AtrBased,
    SwingWithAtrBuffer,
    SwingWithPartialExits // etapa 4.1: TP escolhido via ResistanceScanner com pontuação;
                          // saída ainda é "tudo ou nada" — o motor de saída parcial de
                          // verdade entra só na etapa 4.3
}