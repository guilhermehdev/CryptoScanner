# Pressão Compradora (experimental)

A coluna substitui o rótulo Varejo e mostra uma nota de 0 a 100 de confirmação
compradora nos futuros USDⓈ-M da Binance. Não identifica pequenos investidores,
não é probabilidade de lucro e não tem um corte validado de compra.

## Dados e janela

- Seis candles **fechados** de 5 minutos (30 minutos), iguais nos perfis Swing e Intraday.
- Vinte candles anteriores como referência de volume, preço médio ponderado pelo volume
  dos fechamentos e amplitude verdadeira média (ATR simples).
- OI no começo e no fim dos mesmos 30 minutos; timestamps agrupados em 5 minutos.
- Uma atualização atrasada de OI é tolerada. Mais de 10 minutos sem fechamento comum,
  falta de dados, lacunas ou volume inválido produzem `—`, nunca um 50 artificial.
- A nota atualiza no scan; o tooltip informa o horário de referência. Não acompanha cada tick.

Fonte: [documentação de mercado USDⓈ-M da Binance](https://developers.binance.com/en/docs/catalog/core-trading-derivatives-trading-usd-s-m-futures/api/rest-api/market-data).
Usa `/fapi/v1/klines` (volume total e volume de compras taker) e
`/futures/data/openInterestHist` (quantidade de contratos, não valor nocional).

## Fórmula inicial, ainda sem calibração histórica

Todos os componentes são limitados a 0–100:

| Componente | Peso | Fórmula |
|---|---:|---|
| Agressão | 45% | 50 + (fração de compras − 0,5) × 250 |
| Persistência | 15% | Percentual dos 6 períodos com compras acima de 50%; empate conta metade |
| Resposta do preço | 20% | 50 + (fechamento − abertura nos 30 min) / ATR de referência × 25 |
| Volume | 10% | 50 + direção do preço × clamp((volume relativo − 1) × 50, 0, 50) |
| OI | 10% | 50 + direção do preço × clamp(variação percentual de OI × 25, 0, 50) |

Acima de 3 ATR do preço médio de referência, desconta 5 pontos por ATR adicional,
até 25 pontos. Sem predominância compradora ou sem alta do preço, a nota fica
limitada a 50. Sem crescimento de OI, fica limitada a 75. Estes limites são
heurísticas de composição, **não limiares de entrada**.

O novo cálculo é apenas uma coluna de confirmação. Não modifica o OpportunityScore,
os alertas, a elegibilidade ou a regra COMPRA/COMPRA+. A contribuição legada de
RetailFlowScore no OpportunityScore permanece separada para não alterar a estratégia
existente durante esta avaliação. Funding não compõe a nova nota.

## Verificação e próxima etapa

`dotnet run --project tests/BuyingPressure.Checks` executa verificações determinísticas
de direção, persistência, esticamento, dados ausentes, parsing e cancelamento.
Essas verificações não demonstram rentabilidade.

Antes de transformar a nota em gatilho: registrar snapshots e resultados futuros,
separar treino e teste cronologicamente, avaliar cada perfil e regime, incluir taxas
e slippage e comparar com a estratégia sem esse filtro. O histórico de OI disponível
na API é limitado; o backtest atual não reconstrói automaticamente estes dados.
