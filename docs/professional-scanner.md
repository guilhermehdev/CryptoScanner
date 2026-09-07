# Fluxo de sinais e validação — versão 7

## O que mudou

O limite de 30 linhas mais favoritos é somente visual. Toda análise válida do universo consultado passa pela elegibilidade. A tabela ScanRuns registra horários, universo solicitado, análises válidas, erros identificados por moeda/etapa, tipos de candidato, aprovados, duplicados, gravados e ativos que falharam em apenas um filtro. Os contadores não são etapas mutuamente exclusivas; não devem ser somados. A exportação do laboratório versão 3 inclui varreduras.csv. O laboratório continua observando exclusivamente o grid.

A gravação de um sinal é uma única instrução condicional no SQLite, evitando concorrência entre verificação e inserção. A identidade usa moeda, perfil e estratégia/contexto durante a janela de repetição. Mudança do rótulo COMPRA para COMPRA+ não cria outro sinal da mesma estratégia. Há validação de cotação, níveis e R/R no preço efetivo antes da gravação. Aprovados nos filtros e efetivamente gravados são contagens diferentes.

## Estratégias de compra

Rompimento: fechamento acima da máxima dos 50 candles anteriores, consolidação anterior ao sinal, stop estrutural e alvos estruturais com parciais. Repique: tendência de alta com martelo, engolfo comprador ou varredura de liquidez; invalidação abaixo da mínima dos cinco candles recentes, com folga de 0,5 ATR. Não exige consolidação. A seleção automática prioriza rompimento quando presente; caso contrário verifica repique. Os parâmetros são hipóteses explícitas, não novos parâmetros otimizados. Pisos de volume, score, alvo e R/R foram preservados.

ScannerProfiles é a fonte comum dos limites ao vivo. Na opção Automática (scanner) do backtest, esses limites e o prazo do perfil prevalecem, o modo é de parciais e a direção é compra. Opções individuais permitem pesquisa com os campos customizados. Legado mantém os experimentos anteriores. A pontuação das estratégias novas usa indicadores disponíveis historicamente, sem o bônus de fluxo futuro indisponível no passado; pressão compradora continua coletada e usada pelo laboratório. FeaturesJson identifica separated-entries-v3.

## Execução e histórico

Novos sinais automáticos persistem ExecutionJson e usam LabSimulation: ticket hipotético de 1.000, taxa de 0,10% e slippage de 0,05% por lado, parciais 40/40/20, stop na entrada somente após TP2. O acompanhamento roda junto ao relógio do laboratório; quando o aplicativo fica fechado não há observação e a próxima cotação marca lacuna. Atualizações usam comparação do estado anterior para impedir que uma escrita atrasada reverta uma parcial. Registros anteriores sem ExecutionJson mantêm seu avaliador legado: não foram reinterpretados retroativamente.

Backtest v7: só usa candles diários fechados no instante da decisão, entra na abertura seguinte ao candle de sinal, valida geometria da nova entrada, desconta os mesmos custos e move stop após TP2. Stop ultrapassado na abertura usa o preço adverso da abertura. Em candle ambíguo prioriza stop; se TP2 e retorno ao novo stop couberem no mesmo candle, encerra o restante conservadoramente. Não há como recuperar a ordem intrabar exata usando somente OHLC. Horários de saída representam observação ao fechar o candle. O backtest e a simulação ao vivo têm a mesma política de custos/parciais, mas usam resoluções diferentes (OHLC e cotações): não se espera igualdade em candles ambíguos.

## Validação cronológica

No Backtest, marque Separar períodos e selecione a data de início da validação entre início e fim. A primeira execução é salva como calibração, a segunda como validação posterior; parâmetros são os mesmos e o estado das operações é reiniciado na divisão. O final de cada período encerra posições abertas com EOT. Não há otimização automática nem seleção usando o resultado da validação. Os testes automatizados usam dados controlados; desempenho em preços reais ainda precisa ser medido nos períodos e universo escolhidos. Usar top por volume atual para estudar anos anteriores introduz viés de seleção; fixe e documente o universo utilizado.

## Estatísticas e limites

Estatísticas por operação continuam sendo somas de retornos, não patrimônio. A carteira limitada usa 10.000 USDT, tickets de 1.000 e cinco vagas, ordenando entradas simultâneas por score e símbolo. Reserva o ticket até o encerramento completo e liquida o resultado líquido; mostra saldo final, aceitos/rejeitados e queda do saldo realizado. Não representa marcação a mercado contínua e não libera parciais antecipadamente. A avaliação de queda real com posições abertas e fluxo completo de cada parcial permanece uma extensão necessária antes de usar essa carteira como avaliação de risco operacional.

Nenhum parâmetro foi declarado vencedor. Compare resultados v7 separadamente dos anteriores e confira lacunas e quantidade de amostras antes de promover regras.
