namespace CryptoScanner.Core.Configuration;

public enum RiskCalculationMode
{
    SwingBased,
    AtrBased,
    SwingWithAtrBuffer,
    SwingWithPartialExits, // etapa 4.1: TP escolhido via ResistanceScanner com pontuação;
                           // saída ainda é "tudo ou nada" — o motor de saída parcial de
                           // verdade entra só na etapa 4.3
    MeanReversionScalp, // estratégia nova pro perfil Scalp — alvo é a volta pra EMA21, não
                        // resistência estrutural (mal calibrada em janelas de 15min, como
                        // identificado na investigação do Scalp com rompimento clássico)
    BollingerReversal // Fase A do lado de venda — banda superior + resistência como zona de
                      // gatilho (não alvo), exige rejeição confirmada. TP1 = banda média.
                      // V1: só TP1 (fechamento único) — TP2/TP3 (suporte estrutural, próximo
                      // suporte) ficam pra quando a engine de saída parcial reconhecer direção.
}