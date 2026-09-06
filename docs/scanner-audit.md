# Auditoria do scanner — candles fechados v2

## Correções

- A leitura ao vivo de klines descarta candles cujo horário de fechamento não é anterior ao início da consulta. Volume, tendência e padrões passam a usar candles completos, incluindo BTC e buscas manuais. O volume relativo mantém a fórmula: volume do último candle fechado dividido pela média dos 19 anteriores. Não houve redução do piso de volume. O método pode retornar um candle a menos que o limite solicitado.
- O gatilho de rompimento usa máxima/mínima dos 50 candles anteriores ao sinal; o caminho curto usa a janela do perfil, também anterior. O alvo de saída continua sendo calculado separadamente. Antes, comparar a entrada ao alvo acima dela impossibilitava o rompimento clássico nesse caminho; incluir o candle no próprio nível também impedia o caminho curto. A consolidação é medida antes do candle de sinal.
- O preço ao vivo fica na coluna Atual; Preço análise preserva a base usada para alvo %, stop % e R/R. O websocket não altera mais o snapshot analítico. A entrada simulada continua consultando preço fresco.
- R/R continua sendo distância ao alvo dividida pela distância ao stop. Alvos próximos e stops distantes podem legitimamente produzir valores baixos. Não foram deslocados níveis nem reduzidos critérios para forçar elegibilidade. Níveis inválidos e parciais invertidas são bloqueados explicitamente.
- O tooltip de rejeição usa a mesma avaliação e os limiares do perfil ativo. Contexto substitui Romp, pois força relativa não significa rompimento. Score ranking explicita que pontuação não é probabilidade nem aprovação de entrada.

## Efeito nos dados

As novas oportunidades exportadas carregam AnalysisVersion = closed-candles-v2 em FeaturesJson. Históricos anteriores permanecem intactos e devem ser separados na análise. O cálculo compartilhado de gatilho e consolidação também afeta novos backtests; resultados anteriores precisam ser reavaliados antes de comparação. O preço de análise representa o último candle fechado (4h no Swing), não uma cotação ao vivo. Pressão compradora continua tendo sua própria janela de 30 minutos, descrita no tooltip. Nenhuma garantia de surgimento de sinais: os filtros restantes continuam exigidos.

## Verificação

Os testes Scanner.AuditChecks usam respostas HTTP controladas com candle parcial, volume conhecido, rompimento com consolidação prévia, alvo separado, proporção de risco, atualização de preço e explicação de rejeição. Não dependem de preços reais ou de acesso à Binance.
