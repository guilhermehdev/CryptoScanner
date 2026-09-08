# Diagnóstico dos limites das resistências

O scanner passa a guardar, além do preço médio da resistência, o menor e o maior topo observado no agrupamento que formou a zona. A execução continua usando o mesmo preço médio como alvo. Portanto, esta versão adiciona medição sem liberar sinais nem alterar trades.

Cada gatilho de rompimento ou repique é classificado como entrada abaixo, dentro ou acima da zona escolhida. O arquivo `.diagnosticos.json` inclui os totais por estratégia e, nos exemplos rejeitados, a zona completa com preço médio, limites, quantidade de toques, score e último teste.

Em zonas com um único pivô, os dois limites coincidem. Em zonas combinadas, inclusive multi-timeframe, os limites abrangem os extremos observados dos grupos fundidos. O preço médio e a pontuação mantêm a lógica anterior.

A reprodução histórica confirmou os mesmos preços de entrada, stop e alvo nos quatro casos verificados. CHZUSDT entrou em 0,008072 dentro da zona 0,008058–0,008100, embora o centro 0,008079 estivesse ligeiramente acima da entrada. Isso mostra por que um ponto único não descrevia corretamente o contexto.

Esta instrumentação ainda não decide como operar uma entrada dentro da zona. Ignorar a primeira resistência ou selecionar uma zona distante apenas para elevar o R/R não foi implementado. O próximo relatório deve medir a frequência e os resultados associados a cada posição antes de qualquer mudança de produção.
