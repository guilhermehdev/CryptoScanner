# Gatilho e stop na mesma estrutura — experimento v1

Hipótese fixa para comparação, sem calibração por resultados. Motor histórico 10. A opção não muda o scanner ao vivo e vem desligada. Os modelos de score, alvos estruturais, parciais, custos, prazo e filtros permanecem os da configuração escolhida. O componente numérico de score do setup permanece o modelo anterior; este experimento altera a confirmação do gatilho e a invalidação, não recalibra o score.

## Regras

- Rompimento: faixa dos 20 candles anteriores ao sinal; amplitude máxima de 5% sobre a mínima; fechamento atual acima da máxima dessa faixa. Stop na mínima da mesma faixa menos 0,5 ATR. Esta versão troca tanto a janela do gatilho (antes 50) quanto a do stop para alinhá-las à consolidação. Não é apenas um teste de distância de stop.
- Repique: suporte fixado pela mínima de 20 candles anteriores aos cinco candles da correção (o quinto é o sinal). Exige estrutura de alta, queda de pelo menos um dos quatro fechamentos anteriores em relação ao fechamento anterior à correção, toque no suporte com tolerância de 0,5 ATR e nenhum fechamento da correção abaixo do suporte menos essa tolerância. O candle atual precisa ser de alta, fechar acima do suporte e acima da máxima do candle anterior. Stop na mínima dos cinco candles menos 0,5 ATR.
- Automática: rompimento confirmado tem prioridade; caso contrário avalia repique. Sem ATR positivo ou com menos de 26 candles, não confirma gatilho.
- Alvos não são afastados para produzir R/R suficiente. Se o alvo estrutural continuar próximo, a entrada continua rejeitada.

## Comparação

1. Escolha Swing, as mesmas moedas e somente a calibração (por exemplo, 01/01/2020 a 01/01/2024). Não marque Separar períodos durante a escolha de regras.
2. Clique em Copiar parâmetros do scanner. Isso seleciona Automática (experimental), preenche os critérios do perfil e desmarca os experimentos.
3. Execute a referência e exporte CSV e diagnósticos.
4. Marque Gatilho e stop na mesma estrutura (teste), sem alterar os outros controles. Execute e exporte novamente.
5. Compare quantidade por estratégia, rejeições, resultado após custos e carteira. O histórico e a assinatura distinguem referência e experimento; o JSON contém StructuralEntryExperiment.

Automática (scanner) não aceita experimentos; use a opção Automática (experimental), Rompimento ou Repique. Para isolar as duas estratégias, repita com cada seleção, mantendo os outros controles iguais. Alterar o perfil exige copiar novamente os parâmetros.

Os testes automatizados usam candles controlados para comprovar o comportamento das regras; não demonstram rentabilidade. Nenhum backtest histórico de desempenho foi executado nesta entrega. O período de 2024–2026 já foi observado e não deve ser tratado como validação inédita. Uma eventual configuração escolhida ainda precisa de acompanhamento futuro sem reajustes retrospectivos.
