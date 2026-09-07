# Auditoria de entrada, stop e alvo — 07/09/2026

Fonte: laboratorio-20260907-105738.zip, arquivos oportunidades.csv e varreduras.csv. Nenhum candle foi reconstruído a partir de preços atuais.

## Casos disponíveis

| Caso (horário de Brasília) | Entrada de análise | Stop | TP2 | R/R recalculado | Conclusão |
|---|---:|---:|---:|---:|---|
| SOLVUSDT, Swing, 07/09 05:00 | 0,00401 | 0,003467142857 | 0,00428 | 0,49737 | Níveis ordenados, retorno insuficiente frente ao risco. Mínimo configurado: 2. |
| ORCAUSDT, Swing, 07/09 09:00 | 1,572 | 1,327571428571 | 1,738 | 0,67914 | Níveis ordenados, retorno insuficiente frente ao risco. Mínimo configurado: 2. |
| CAKEUSDT, Intraday, 07/09 05:00 | Não exportada | Não exportado | Não exportado | Não disponível | Contador informa bloqueio exclusivo por distância. Não é possível reproduzir os preços desse caso. |
| XLMUSDT, Swing, 07/09 05:00 | Não exportada | Não exportado | Não exportado | Não disponível | Contador informa bloqueio exclusivo pelo antigo critério combinado de R/R e níveis. Não é possível separar retroativamente. |

Os registros Swing de CAKE não substituem seu registro Intraday. A exportação antiga contém preços somente das moedas do grid. SOLV e ORCA não revelaram erro aritmético no R/R. Alterar seus stops para obter aprovação seria uma mudança de estratégia, não uma correção matemática.

## Correções e instrumentação

- `FailedInvalidLevels` identifica geometria inválida; `FailedRiskReward` passa a identificar somente R/R abaixo do mínimo quando os níveis são válidos. Tooltips, resumo, contadores e backtest distinguem os motivos. Diagnósticos antigos v1 conservam a semântica combinada.
- `scan-funnel-v2` grava regime, parâmetros e análise completa de todas as moedas analisadas em `varreduras.csv`, dentro de `DiagnosticsJson`. Permite reproduzir a elegibilidade fora do grid. Não inclui candles brutos nem permite reconstruir indicadores do zero.
- Backtest de compra recalcula R/R usando a abertura seguinte com slippage, rejeita entradas que deixam de atender ao mínimo e registra distâncias coerentes com esse preço. Antes, os números de entrada eram herdados do candle de análise. Scanner usa a mesma função para o R/R da execução.
- `EntryRejected` conta rejeições de entrada no backtest. No scanner, motivos detalhados continuam em `Errors`.
- Motor histórico versão 8 distingue novos resultados dos testes antigos no histórico.

## Comparação configurável

1. Escolha o perfil, as moedas e os períodos. Use uma lista fixa de moedas para comparar alternativas.
2. Clique em **Copiar parâmetros do scanner**. O modo fica **Automática (experimental)** e os limiares são preenchidos com os do perfil escolhido. Se mudar o perfil, copie novamente.
3. Execute a referência sem alterar a distância. Para testar outra distância percentual, altere **Dist. Resist. mín. Pontuada %**. Para testar volatilidade, marque **Alvo mínimo em ATR** e informe um múltiplo positivo. Esse múltiplo substitui o piso percentual, sem mudar os alvos estruturais, o stop ou o R/R mínimo. Ausência de ATR bloqueia a avaliação experimental. Modos de reversão continuam isentos desse piso.
4. Compare alternativas primeiro somente no período de calibração. Escolha uma configuração antes de olhar o período posterior.
5. Rode a configuração escolhida no período posterior, sem novos ajustes. **Separar períodos** também permite executar ambos com parâmetros fixos, mas não é um otimizador nem impede que o operador consulte a validação repetidamente.

**Automática (scanner)** continua reproduzindo os parâmetros ao vivo. O piso de 15% no Intraday não foi reduzido em produção. Os testes experimentais ficam identificados no histórico por estratégia, piso e assinatura própria.

Esta entrega não executou uma validação histórica de rentabilidade. A carteira histórica ainda usa a aproximação documentada em professional-scanner.md, com drawdown realizado e capital retido até o encerramento. Os novos controles não eliminam essas limitações.
