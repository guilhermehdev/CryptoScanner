# Laboratório de estratégias — geração inicial

O botão Laboratório apresenta cinco variantes fixas que simulam compras usando somente os ativos do grid visível no fim de cada atualização ou busca manual. O perfil e o filtro de favoritos são respeitados. Cada ativo/perfil é observado uma vez por janela de cinco minutos; rejeições e pausas também consomem essa oportunidade.

A referência experimental exige score de oportunidade >= 55 e pressão compradora >= 50. Duas variantes mudam a pressão mínima para 40 e 60; outras duas multiplicam a distância do stop por 0,85 e 1,15. Esta referência não reproduz todos os filtros de elegibilidade do scanner. Os indicadores e níveis são congelados antes de consultar o preço de entrada, evitando alterações posteriores no registro.

Cada variante começa com 10.000 USDT fictícios, aplica 1.000 por entrada e admite cinco posições simultâneas, sem duplicar o mesmo ativo. As simulações descontam taxa de 0,10% e slippage de 0,05% em cada lado. Entradas com preço ausente, atrasado ou níveis inconsistentes são rejeitadas. TP1 realiza 40%, TP2 realiza 40% e move o stop para a entrada; TP3 realiza o restante. Há encerramento pelo prazo do perfil. Estes custos são hipóteses do simulador.

As posições abertas são acompanhadas independentemente do grid atual, aproximadamente a cada 30 segundos enquanto o aplicativo funciona. Um stop ultrapassado encerra ao preço observado, com custos. Não há reconstrução de movimentos entre consultas: intervalos acima de 90 segundos recebem marca de lacuna. O aplicativo fechado não acompanha preços; ao reabrir, retoma as posições persistidas e sinaliza a interrupção quando houver nova cotação. Resultados com lacunas precisam ser interpretados separadamente.

O mesmo SQLite do aplicativo guarda parâmetros, capital, oportunidades, indicadores, decisões aceitas/rejeitadas, posições e saídas nas tabelas Lab*. Os trades manuais permanecem separados. Pausar impede novas entradas e mantém o acompanhamento das posições abertas. A tela mostra os últimos 200 trades e decisões; os totais consideram todo o histórico. Use Atualizar para recarregar a tela. Patrimônio inclui liquidação estimada das posições abertas, e queda máxima é calculada nas observações disponíveis.

Esta fase coleta experiências e compara variantes. Ainda não cria gerações automaticamente, não promove vencedores e não muda sinais do ranking. A próxima fase requer avaliação em períodos posteriores aos usados para escolher parâmetros, amostra suficiente e tratamento de custos e lacunas antes de habilitar seleção e novas mutações.

Validação: dotnet run --project tests/StrategyLab.Checks/StrategyLab.Checks.csproj

## Exportação para análise

Clique em **Exportar para análise**, escolha o destino e envie o ZIP gerado. O arquivo contém resumo.csv, variantes.csv, oportunidades.csv, decisoes.csv, trades.csv, saidas.csv, configuracao.csv, manifesto.json e LEIA-ME.txt. Exporta todo o histórico em uma fotografia consistente do banco, incluindo perdas, rejeições, custos, indicadores em JSON e posições abertas. Horários são Unix em milissegundos UTC; números usam ponto decimal. O manifesto informa contagens e horário de exportação. A gravação usa arquivo temporário e só substitui o destino após terminar com sucesso. Não inclui o diário manual nem credenciais. A exportação não altera os resultados do laboratório.

## Testes sem vaga

A aba **Testes sem vaga** mostra simulações independentes de novas oportunidades rejeitadas por limite de posições ou capital, desde que passem também na validação dos preços e níveis. Não há alteração nos saldos ou resultados das cinco carteiras. Cada teste usa o mesmo ticket fictício e motor de custos, parciais, stop e prazo. Um único teste paralelo por moeda/variante pode ficar aberto, mesmo entre perfis, para limitar repetição de sinais sobrepostos. A pausa bloqueia novas entradas dos dois grupos; posições existentes continuam monitoradas e persistidas. As moedas com testes abertos são acompanhadas mesmo fora do grid. Não há simulação retroativa dos registros antigos.

O ZIP versão 2 inclui testes_sem_vaga.csv, saidas_sem_vaga.csv e resumo_sem_vaga.csv. Vincule OpportunityId a oportunidades.Id para analisar pressão, RSI, volume, regime e níveis congelados. O lucro agregado desse grupo não é retorno de uma carteira financiável e não deve ser somado ao patrimônio. Considere sinais correlacionados e lacunas; esse grupo representa apenas rejeições por capacidade. Ele ainda não gera mutações automaticamente.
