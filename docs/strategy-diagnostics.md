# Diagnóstico por estratégia

Motor histórico 9, diagnósticos scan-funnel-v3. Não altera os gatilhos ou os limiares ao vivo. O objetivo é identificar qual combinação de critérios elimina os rompimentos e caracterizar os repiques antes de escolher mudanças de estratégia.

## Referência atual: Breakout Trend Long

A validação passa a usar uma hipótese única e congelada: **Swing Long em candles de 4h**, com fechamento acima da máxima anterior, consolidação prévia, stop estrutural, saídas parciais, score mínimo 60, volume relativo mínimo 1,30×, alvo pontuado mínimo 4%, R/R mínimo 2 e stop máximo 25%. Repique, reteste Intraday, ATR máximo, multi-timeframe e demais experimentos ficam desligados.

No Backtest, o primeiro perfil abre essa referência como **Breakout Trend Long · base clássica** e separa por padrão dois períodos cronológicos: calibração e validação posterior. Quando o modo **Base fixa da estratégia** está selecionado, os campos de limiar não alteram a execução: o teste só aceita Swing Long com risco Swing + Resistência Pontuada. Reteste Intraday, Short e outras geometrias precisam de perfis de pesquisa próprios. O resultado da validação não deve alterar esta configuração. A estratégia não muda o scanner ao vivo até sobreviver às duas janelas e à simulação ao vivo.

O resumo do backtest separa a estratégia efetivamente escolhida pelo analisador. Em Automática, um rompimento tem prioridade sobre repique quando ambos aparecem. Cada grupo apresenta avaliações, gatilhos confirmados, elegíveis antes da cotação de entrada e reprovações apenas entre gatilhos confirmados. Essas reprovações podem se sobrepor. O arquivo também guarda contagens brutas de rompimento e repique; elas podem se sobrepor.

Ao exportar trades, são gravados dois arquivos:

- CSV com operações e três novas colunas: FavorableBeforeExitCandlePercent, AdverseBeforeExitCandlePercent e ObservedPreExitCandles.
- Arquivo de mesmo nome com extensão .diagnosticos.json: versão do motor, parâmetros e período (execução normal), contadores por estratégia, bloqueios exclusivos por um filtro e até 30 exemplos rejeitados por estratégia. Os exemplos são os primeiros cronologicamente, não uma amostra estatística representativa. Cada exemplo contém moeda, horário da decisão, preço de análise, suporte, resistência, R/R e motivos. Para vendas legadas, suporte representa alvo e resistência representa stop.

É possível exportar os diagnósticos mesmo quando o teste termina com zero trades. Testes antigos não ganham esses dados retroativamente; é necessário executá-los novamente. Em execução com separação cronológica, a exportação corresponde à validação atualmente exibida, não à calibração.

As excursões são percentuais brutos em relação à abertura de entrada e consideram somente candles completos anteriores ao candle de saída. O candle de saída é excluído, pois OHLC não permite saber se uma máxima ocorreu antes ou depois do stop. Também não se incluem custos nesses indicadores de movimento. Zero com ObservedPreExitCandles=0 significa ausência de observação válida, não ausência de movimento. Portanto, não são MFE/MAE completos intrabar e não devem ser usados como se fossem.

Os rótulos da soma dos retornos e da queda dessa soma usam pontos percentuais. Retorno e queda da carteira limitada continuam separados e mantêm a limitação de saldo realizado já descrita na interface.

Próxima coleta: repetir o mesmo perfil, moedas, parâmetros e datas; enviar CSV e .diagnosticos.json juntos. Comparar os bloqueios de rompimento com a trajetória dos repiques, sem selecionar novos parâmetros pelo período de validação já observado.
