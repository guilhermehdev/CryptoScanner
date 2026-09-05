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

## Gravação e vínculo com trades

A partir desta versão, o scan e a busca manual gravam no mesmo SQLite do aplicativo
(`%LOCALAPPDATA%/CryptoScanner/signals.db`), com criação/migração automática:

- `BuyingPressureSnapshots`: uma leitura por ativo, fechamento de 5 minutos e versão
  da fórmula, compartilhada por Swing e Intraday. Guarda horários UTC em milissegundos,
  nota, qualidade, componentes numéricos, referências e entradas completas em JSON.
- `BuyingPressureFailures`: primeira falha por ativo/janela/versão, com os dados recebidos.
  Se uma tentativa posterior recuperar a janela, a nota passa a ter o horário real dessa
  recuperação. Uma leitura já válida nunca é substituída por dados posteriores.
- `BuyingPressureOutcomes`: avaliações de 30, 60, 240 e 1440 minutos após o fechamento
  de referência, com preço exato do candle, retorno bruto, coleta e origem histórica.
- `BuyingPressurePrices`: fechamentos de futuros reaproveitados entre avaliações.

Todo ativo que chega à análise de fluxo é gravado, mesmo que não apareça entre os
melhores candidatos ou não gere trade. O intervalo efetivo depende dos scans:
não existe coleta enquanto o aplicativo está fechado, e scans espaçados deixam lacunas.
O histórico começa com o uso desta versão; não são inventadas notas para janelas perdidas.

Na abertura do trade, o banco escolhe atomicamente a leitura válida mais recente
do mesmo ativo e versão, **coletada antes ou no horário da entrada**, cujo fechamento
não tem mais de 10 minutos. O `BuyingPressureSnapshotId` fica salvo no trade. Sem leitura
válida, o vínculo fica nulo. Trades antigos não são preenchidos retroativamente.
A coluna **Pressão na entrada** no diário exibe a nota vinculada.

Para resultados posteriores, usa-se primeiro o preço dos candles já recebidos. Após
cada scan concluído, o aplicativo busca até 20 fechamentos históricos pendentes,
sempre no endpoint de futuros e no horário exato, sem substituir por cotação atual.
Tentativas sem sucesso voltam à fila após pelo menos 5 minutos. Pendências e vínculos
sobrevivem ao reinício. Resultados coletados historicamente são marcados como
`Reconstructed=1`; essa marca não transforma a nota original em uma nota reconstruída.

Os retornos são **movimentos brutos de mercado a partir do preço de referência**,
não lucros executáveis nem resultados líquidos de trades. A nota só ficou disponível
em `CollectedAtMs`; a validação deve respeitar esse atraso, além de taxas e slippage.
Falhas de gravação/recuperação aparecem no diagnóstico do scanner. Não há exclusão
automática de registros nesta versão.

## Tela de análise

Abra **Análise da pressão** na barra superior do scanner. O botão **Atualizar** consulta
o histórico local; não faz operações de mercado nem solicita preços à Binance.

- Filtros: datas de **coleta** (dias locais, incluindo o último dia), ativo exato
  ou todos, e resultado de 30 minutos, 1 hora, 4 horas ou 24 horas.
- A versão da fórmula é fixada na versão atual e aparece no resumo; versões diferentes
  não são misturadas. Swing e Intraday compartilham os mesmos registros.
- Faixas de 10 pontos: limite inferior incluso e superior exclusivo, exceto 90–100,
  que inclui 100. As faixas são agrupamentos descritivos, não recomendações de entrada.
- Cada faixa mostra N avaliado, pendências, recuperação atrasada, proporção de retornos
  positivos, média, mínimo, máximo e quantidade de resultados recuperados historicamente.
  Retorno zero entra na média e no denominador, mas não conta como positivo.
- Leituras indisponíveis ficam fora das faixas numéricas e são contadas no resumo.
  Avaliações pendentes não entram nas médias nem são tratadas como retorno zero.
- As estatísticas consideram **todo** o filtro. O histórico detalhado exibe até 500
  leituras mais recentes; selecionar uma linha mostra os componentes do tooltip.
- A coleta e o fechamento da janela aparecem separadamente, no horário local.

Leituras sobrepostas não são observações independentes. Os resultados exibidos são
retornos brutos de mercado; não constituem taxa de acerto de trades. Sem dados,
a tela explica que o scanner precisa acumular leituras. A atualização é manual.

## Verificação e próxima etapa

`dotnet run --project tests/BuyingPressure.Checks` executa verificações determinísticas
de direção, persistência, esticamento, dados ausentes, parsing e cancelamento.
Essas verificações não demonstram rentabilidade.

`dotnet run --project tests/BuyingPressure.HistoryChecks` verifica a migração de banco
antigo, concorrência, deduplicação, vínculos sem informação futura, qualidade dos dados,
recuperação após reinício e retornos em horários exatos, usando bancos temporários.

`dotnet run --project tests/BuyingPressure.AnalysisChecks` verifica filtros, faixas,
pendências, denominadores, proveniência dos resultados e agregação além das 500 linhas.

Antes de transformar a nota em gatilho: acumular snapshots e resultados futuros,
separar treino e teste cronologicamente, avaliar cada perfil e regime, incluir taxas
e slippage e comparar com a estratégia sem esse filtro. O histórico de OI disponível
na API é limitado; o backtest atual não reconstrói automaticamente estes dados.
