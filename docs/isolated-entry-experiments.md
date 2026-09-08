# Testes isolados — motor 11

A lista de experimentos substitui a caixa da estrutura completa. Opções mutuamente exclusivas:

- Referência: mesmas regras anteriores.
- Teste A: somente o stop do rompimento usa a mínima dos 20 candles anteriores ao sinal menos o buffer ATR existente. Gatilho de 50 candles, consolidação, alvos e repique permanecem iguais. O R/R é recalculado com o stop novo.
- Teste B: somente a confirmação do repique passa a exigir engolfo de alta atual, martelo atual ou varredura de mínima no candle atual. A mínima de referência da varredura é exatamente a do detector anterior (dez candles antes da janela de três). Uma varredura de um ou dois candles atrás não basta. Tendência, stop de cinco candles, alvos e rompimento permanecem iguais. Não inclui o novo requisito de correção e suporte da versão completa.
- Estrutura completa: preserva o experimento anterior para reprodução.

Como comparar: usar as mesmas moedas, Swing, 01/01/2020 a 01/01/2024, sem Separar períodos. Copiar parâmetros do scanner primeiro, selecionar Referência, executar e exportar. Repetir selecionando Teste A e depois Teste B, sem copiar os parâmetros novamente nem alterar os demais controles. Exportar CSV e .diagnosticos.json de cada rodada.

O JSON e a assinatura do histórico incluem IsolatedEntryExperiment (0 referência, 1 A, 2 B) e StructuralEntryExperiment (true somente na opção completa). O scanner ao vivo continua com ambos desligados.

Os testes automatizados verificam preservação de gatilhos, stops e alvos de cada variante e a diferença entre varredura atual e antiga. Não foi executado novo teste histórico de rentabilidade nesta entrega. Não escolher regras pelos resultados posteriores já observados.
