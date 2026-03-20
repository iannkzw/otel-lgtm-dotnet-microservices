# State

**Last Updated:** 2026-03-19
**Current Work:** M4 completo - todas as features do roadmap finalizadas. `README.md` criado e validado contra artefatos da baseline, `tools/load-generator/generate-orders.ps1` implementado com spec/design/tasks, ROADMAP atualizado com status DONE para ambas as features finais.

---

## Recent Decisions

### AD-052: Implementacao do gerador de carga fica restrita ao script host-side e ao README minimo (2026-03-19)

**Decision:** A fase de tasks da feature `gerador-de-carga` deve preparar a implementacao futura em nove passos atomicos, com diff funcional restrito a `tools/load-generator/generate-orders.ps1` e a uma referencia curta no `README.md`.
**Reason:** O principal risco desta etapa e reabrir a baseline validada da PoC para acomodar um helper de demonstracao que precisa permanecer pequeno, externo e facilmente revisavel.
**Trade-off:** O gerador continua propositalmente enxuto e sem abstrair cenarios avancados, mas ganha clareza de escopo, verificacoes objetivas e pronta execucao na proxima iteracao.
**Impact:** A proxima iteracao deve implementar apenas entrypoint, validacao de parametros, payload real com `description`, modos `happy` e `latency`, resumo final e referencia minima no `README.md`, sem mudancas em `src/`, compose, collector, processors ou Grafana.

### AD-051: O MVP do gerador deve caber em um unico script PowerShell em `tools/load-generator/` (2026-03-19)

**Decision:** O design da feature `gerador-de-carga` deve concentrar o utilitario em um unico entrypoint `tools/load-generator/generate-orders.ps1`, com funcoes internas para payload, execucao e resumo, sem modulo separado, sem container dedicado e sem configuracao externa.
**Reason:** A feature precisa permanecer pequena, facil de versionar e aderente ao papel de helper de demonstracao. Espalhar o MVP em multiplos artefatos aumentaria manutencao sem ganho proporcional.
**Trade-off:** O script concentra mais responsabilidade interna em um unico arquivo, mas essa compactacao e aceitavel porque o dominio do utilitario e curto e bem delimitado.
**Impact:** A proxima etapa de tasks deve quebrar trabalho dentro do mesmo arquivo e tratar qualquer expansao estrutural como opcional, nao como requisito do MVP.

### AD-050: O modo `latency` deve usar densidade client-side controlada, nao benchmark tooling nem alteracao do servidor (2026-03-19)

**Decision:** O design da feature `gerador-de-carga` deve tratar o modo `latency` como aumento de densidade de requests por batches e concorrencia opcional no cliente PowerShell, sempre contra o endpoint real `POST /orders` e sem mudancas no `OrderService`.
**Reason:** O objetivo de M4 e demonstracao operacional reproduzivel do alerta existente, nao medicao formal de performance nem reabertura de escopo funcional no servidor.
**Trade-off:** As duracoes medidas no host passam a refletir tambem overhead do proprio PowerShell e dos jobs locais, mas isso e aceitavel para a PoC porque o sinal relevante continua sendo o comportamento observavel do sistema real.
**Impact:** A implementacao futura deve evitar endpoints especiais, sleeps internos na aplicacao, ferramentas externas de benchmark e qualquer ajuste em compose ou telemetria para suportar o modo de latencia.

### AD-049: README continua canonico e o gerador de carga entra apenas como helper referenciado (2026-03-19)

**Decision:** A feature `gerador-de-carga` deve complementar o `README.md`, nao substitui-lo. O README permanece como roteiro canonico da demo, enquanto o gerador entra apenas como utilitario versionado para automatizar o passo de emitir requests locais.
**Reason:** O principal risco de M4 neste ponto e criar dois roteiros de demonstracao concorrentes: um documental e outro centrado no script. Isso reduziria a clareza da PoC em vez de melhora-la.
**Trade-off:** A documentacao do gerador precisa permanecer enxuta e subordinada ao walkthrough maior do README, mesmo que isso deixe detalhes internos do utilitario fora do fluxo principal.
**Impact:** O design e a futura implementacao da feature devem concentrar a logica no utilitario e expor no README apenas o comando principal e o contexto minimo de uso.

### AD-048: MVP do gerador de carga deve ser host-side em PowerShell, sem compose dedicado nem SDK .NET local obrigatorio (2026-03-19)

**Decision:** O caminho principal da feature `gerador-de-carga` deve ser um script PowerShell versionado no repositorio, executado no host apos o `docker compose up`, chamando diretamente `http://localhost:8080/orders`.
**Reason:** Esta e a opcao mais aderente ao ambiente validado da PoC no Windows, reutiliza os comandos PowerShell ja comprovados nas validacoes recentes e evita complicacoes de rede de um container temporario mirando `localhost`.
**Trade-off:** O MVP fica orientado ao host Windows e nao resolve paridade imediata para shells POSIX, mas ganha simplicidade, reprodutibilidade e zero dependencia de SDK .NET ou ferramenta externa de benchmark.
**Impact:** O design da feature deve priorizar um unico entrypoint host-side, manter o utilitario fora do compose funcional e tratar bash/curl ou container temporario apenas como opcoes futuras, nao obrigatorias.

### AD-047: Implementacao do README deve tocar apenas `README.md` e validar cada secao contra artefatos versionados da baseline (2026-03-19)

**Decision:** A etapa de tasks da feature `readme-poc` deve preparar a implementacao do README como uma mudanca concentrada em `README.md`, com tarefas separadas para bootstrap, matriz host versus rede interna, fluxo feliz, traces, metricas, logs, alertas e troubleshooting.
**Reason:** O principal risco desta feature e transformar uma mudanca puramente documental em churn desnecessario sobre compose, alertas ou codigo de aplicacao. Fixar a fronteira em `README.md` mantem a implementacao pequena, verificavel e fiel ao milestone M4.
**Trade-off:** A iteracao de implementacao fica totalmente dependente da qualidade dos artefatos versionados ja existentes; qualquer lacuna funcional descoberta nessa fase precisara ser tratada fora desta feature documental.
**Impact:** O proximo passo passa a ser editar somente `README.md`, conferindo cada secao diretamente com `docker-compose.yaml`, `src/OrderService/Program.cs`, `grafana/dashboards/otel-poc-overview.json`, `grafana/provisioning/alerting/` e `tools/alert-webhook-mock/server.py`.

### AD-046: README final da PoC deve ser guiado por ordem fixa, comandos reproduziveis e matriz host versus rede interna (2026-03-19)

**Decision:** O design da feature `readme-poc` deve tratar o `README.md` final como documento canonico da PoC, com ordem fixa de secoes, exemplos `docker compose` como caminho principal, exemplos HTTP em PowerShell e uma matriz explicita separando servicos acessiveis no host de endpoints internos da rede Docker.
**Reason:** A principal fragilidade do `README.md` atual nao e falta de volume de texto, mas sim drift em relacao a baseline real do compose e ambiguidade operacional ao misturar URLs locais e endpoints internos, especialmente no fluxo de validacao de alertas.
**Trade-off:** A documentacao fica mais opinativa sobre formato e ambiente validado, mas ganha reproducibilidade e reduz instrucoes enganosas ou impossiveis de executar no estado atual da PoC.
**Impact:** A futura implementacao do `README.md` deve reutilizar apenas artefatos e validacoes ja consolidados, tratar Docker Compose como caminho primario e orientar a verificacao do `alert-webhook-mock` por logs do compose ou inspecao interna, nunca por `localhost`.

### AD-045: README da PoC deve tratar Docker Compose como caminho primario e separar URLs do host de endpoints internos (2026-03-19)

**Decision:** A feature `readme-poc` deve documentar a execucao local da PoC a partir do compose atual, assumindo Docker Compose como caminho primario de bootstrap e distinguindo explicitamente os servicos expostos no host (`localhost:3000`, `localhost:8080`, `localhost:4317`, `localhost:4318`) dos endpoints que existem apenas na rede Docker, como `http://alert-webhook-mock:8080`.
**Reason:** O `README.md` atual ainda reflete uma baseline anterior e, sem essa separacao, tende a induzir passos incorretos de demo e troubleshooting, principalmente ao sugerir acessos `localhost` para servicos que hoje sao apenas internos.
**Trade-off:** A documentacao fica um pouco mais explicita sobre topologia de rede e limita o roteiro a validacoes realmente reproduziveis no ambiente atual, em vez de simplificacoes artificiais.
**Impact:** O futuro `README.md` deve listar URLs locais reais, marcar claramente os endpoints internos do compose e orientar a verificacao do `alert-webhook-mock` por logs do compose ou inspecao interna, sem exigir publicacao de nova porta.

---

### AD-029: Dashboard da PoC usa diretório dedicado `/otel-lgtm/dashboards` criado por bind mount (2026-03-19)

**Decision:** Montar o JSON versionado da PoC em `/otel-lgtm/dashboards/otel-poc-overview.json` por bind mount read-only, mesmo que o diretório `/otel-lgtm/dashboards` não exista previamente na imagem base.
**Reason:** O provider isolado da PoC precisa apontar para um caminho separado dos dashboards nativos da imagem e a inspeção de runtime mostrou que o provisioning real fica em `/otel-lgtm/grafana/conf/provisioning`, enquanto os dashboards da PoC não devem sobrescrever `grafana-dashboards.yaml` nem os assets embarcados.
**Trade-off:** A solução depende do comportamento padrão do Docker de materializar o caminho-alvo do bind mount no container, então a validação da feature precisa confirmar explicitamente a presença do arquivo montado após o restart do `lgtm`.
**Impact:** O diff funcional permaneceu restrito ao `docker-compose.yaml` e aos artefatos versionados em `grafana/`, sem tocar collector, processors, datasource nativo ou serviços .NET.

### AD-001: Reutilizar otel-demo-main como backend de observabilidade (2026-03-19)

**Decision:** Os serviços .NET vão exportar telemetria para o OTel Collector já configurado no `otel-demo-main`, sem criar um collector paralelo.
**Reason:** Reaproveitar a configuração existente (tail sampling, pipelines, LGTM) economiza tempo e demonstra integração com infra real.
**Trade-off:** Os serviços .NET ficam dependentes dos containers do `otel-demo-main` rodando. Acoplamento de ambiente.
**Impact:** O `docker-compose.yaml` dos serviços .NET deve fazer referência (`extends` ou rede compartilhada) ao compose do `otel-demo-main`, ou ambos devem ser unificados em um único compose.

---

### AD-002: Kafka como brocker de eventos, sem Schema Registry (2026-03-19)

**Decision:** Usar serialização JSON simples nas mensagens Kafka, sem Avro ou Schema Registry.
**Reason:** Escopo da PoC é observabilidade, não serialização de eventos. Simplifica a infraestrutura.
**Trade-off:** Sem contrato formal de schema. Aceitável para PoC.
**Impact:** Apenas `Confluent.Kafka` como dependência. Headers Kafka serão usados para propagação de trace context (W3C TraceContext).

---

### AD-003: Propagação de contexto Kafka via headers W3C (2026-03-19)

**Decision:** Implementar utilitário `KafkaTracingHelper` nos serviços para injetar (`traceparent`, `tracestate`) nos headers ao produzir e extrair ao consumir.
**Reason:** OpenTelemetry .NET não possui instrumentação automática para Confluent.Kafka. A propagação manual via W3C TraceContext é a abordagem recomendada.
**Trade-off:** Código de infraestrutura manual. Pode virar biblioteca compartilhada futuramente.
**Impact:** Cada producer/consumer Kafka deve chamar o helper. Um `shared` project ou pasta `/Common` pode ser criada para compartilhar o helper.

---

### AD-004: PostgreSQL como banco de dados único (2026-03-19)

**Decision:** Um único PostgreSQL compartilhado entre OrderService e NotificationWorker, com schemas ou tabelas separadas por serviço.
**Reason:** PoC — minimizar infra. O objetivo é mostrar instrumentação de banco, não isolamento de dados.
**Trade-off:** Não reflete arquitetura real de microsserviços (banco por serviço).
**Impact:** Connection string única nos dois serviços. EF Core com migrations separadas por DbContext.

---

### AD-005: .NET 10 com Minimal API para OrderService (2026-03-19)

**Decision:** Usar Minimal API (não Controllers) para o OrderService.
**Reason:** Mais alinhado com .NET moderno, menos boilerplate, endpoints simples.
**Trade-off:** Menos familiar para devs acostumados com MVC.
**Impact:** Configuração de OpenTelemetry.Instrumentation.AspNetCore é idêntica.

---

### AD-006: Implementar a infra Compose em duas etapas (2026-03-19)

**Decision:** Adicionar primeiro a infraestrutura de terceiros (`zookeeper`, `kafka`, `postgres`) ao `docker-compose.yaml` e deixar a inclusão dos serviços .NET para a feature `.NET Solution`.
**Reason:** O repositório ainda não contém `otel-poc.sln`, projetos em `src/` nem Dockerfiles. Referenciar builds inexistentes quebraria o compose imediatamente.
**Trade-off:** A feature de infra fica parcialmente entregue até a próxima feature ser implementada.
**Impact:** O roadmap e os tasks da feature precisam explicitar o bloqueio; o próximo passo obrigatório é gerar a solution .NET.

---

### AD-007: Validar net10 via SDK 10 em container quando o host estiver defasado (2026-03-19)

**Decision:** Manter a solution em `net10.0`, fixar `global.json` em `10.0.100` com `rollForward: latestFeature` e usar `mcr.microsoft.com/dotnet/sdk:10.0` para validar a solution enquanto o host local não tiver SDK 10 instalado.
**Reason:** A spec da feature exige .NET 10; o ambiente Windows local só possui SDKs até 9.x, mas Docker está disponível e já é dependência da PoC.
**Trade-off:** O build validado da solution passa a depender de Docker até o host receber SDK 10.
**Impact:** `dotnet build otel-poc.sln` funciona em container com SDK 10 e os Dockerfiles continuam aderentes ao design da feature.

---

### AD-008: Usar contexto de build da raiz no Compose para os serviços .NET (2026-03-19)

**Decision:** Configurar `order-service`, `processing-worker` e `notification-worker` no `docker-compose.yaml` com `build.context: .` e `dockerfile` apontando para cada Dockerfile em `src/`.
**Reason:** Os Dockerfiles copiam `global.json`, `Directory.Build.props` e os `.csproj` a partir da raiz do repositório; usar o diretório do serviço como contexto quebraria o build.
**Trade-off:** O contexto enviado ao Docker é maior do que o diretório isolado de cada serviço.
**Impact:** `docker compose build` dos três serviços passa sem modificar os Dockerfiles existentes.

---

### AD-009: Corrigir baseline do collector para a versão atual da imagem (2026-03-19)

**Decision:** Remover a chave `file_format` de `otelcol.yaml` e ajustar `drop-health-checks.yaml` para `sampling_percentage: 0`.
**Reason:** A imagem atual `otel/opentelemetry-collector-contrib:latest` falhava ao iniciar com `file_format`, o que bloqueava totalmente a validação OTLP; além disso, a policy anterior mantinha 1% dos health checks, contrariando a spec da feature.
**Trade-off:** Pequena alteração no baseline do collector para manter compatibilidade com a imagem `latest` e aderência funcional à spec de M1.
**Impact:** O `otelcol` voltou a iniciar normalmente e o Tempo deixou de retornar traces recentes de `/health` durante a validação da feature.

---

### AD-010: Emitir spans manuais de heartbeat nos workers para fechar M1 (2026-03-19)

**Decision:** Adicionar `ActivitySource` manual em `ProcessingWorker` e `NotificationWorker`, com um span por ciclo de heartbeat, e registrar esses sources no bootstrap OTel.
**Reason:** M1 exigia comprovar os 3 `service.name` como sources distintos no Tempo, mas os workers ainda não executam HTTP, Kafka ou outras operações já instrumentadas.
**Trade-off:** Os spans de heartbeat servem apenas para validar a presença dos serviços no pipeline OTel; eles não substituem o trace distribuído real que será implementado em M2.
**Impact:** A task T8 da feature `otel-bootstrap` pôde ser concluída sem antecipar a lógica de negócio planejada para o próximo milestone.

---

### AD-011: OrderService deve persistir antes de publicar no Kafka em M2 (2026-03-19)

**Decision:** A feature `order-service-api-persistencia` vai salvar o pedido no PostgreSQL com `status = pending_publish` antes de tentar publicar no topic `orders`.
**Reason:** O milestone precisa que `GET /orders/{id}` tenha uma fonte de verdade persistida e que falhas de publish sejam observáveis no banco e no trace.
**Trade-off:** Sem transactional outbox ainda existe janela de inconsistência entre banco e Kafka.
**Impact:** O design da feature passa a exigir atualização explícita de status para `published` ou `publish_failed`.

---

### AD-012: GET /orders/{id} é o contrato estável para o ProcessingWorker (2026-03-19)

**Decision:** O `OrderService` vai expor `GET /orders/{id}` com payload mínimo e estável contendo `orderId`, `description`, `status`, `createdAtUtc` e `publishedAtUtc`.
**Reason:** A próxima subfeature de M2 usará essa rota para enriquecimento HTTP no `ProcessingWorker`.
**Trade-off:** O contrato fica intencionalmente enxuto e mockado nesta fase da PoC.
**Impact:** A implementação da API deve refletir diretamente o estado persistido, sem montar respostas derivadas fora do banco.

---

### AD-013: Producer Kafka do OrderService falha rápido para expor `publish_failed` (2026-03-19)

**Decision:** Configurar o producer Kafka do `OrderService` com timeout curto de mensagem/socket.
**Reason:** A feature precisa responder `503` em tempo razoável quando o broker estiver indisponível, em vez de deixar a request HTTP presa por timeout longo do cliente Kafka.
**Trade-off:** Timeout agressivo pode ser mais sensível a instabilidades transitórias, mas é adequado para a PoC e melhora a observabilidade do estado `publish_failed`.
**Impact:** O fluxo de erro pôde ser validado localmente com Kafka parado, persistindo `publish_failed` e devolvendo `503 Service Unavailable`.

---

### AD-014: ProcessingWorker usa o evento Kafka apenas como gatilho e o GET /orders/{id} como fonte de verdade (2026-03-19)

**Decision:** A feature `processing-worker-consumer-http-call` vai consumir do topic `orders` um payload minimo com `orderId`, `description` e `createdAtUtc`, reconstruir o contexto distribuido a partir dos headers W3C e buscar o estado enriquecido do pedido via `GET /orders/{id}` antes de produzir no topic `notifications`.
**Reason:** O contrato ja publicado pelo `OrderService` e enxuto e suficiente para localizar o pedido, enquanto o endpoint HTTP exposto em M2 devolve o estado persistido mais confiavel para enriquecer o processamento.
**Trade-off:** O worker passa a depender de uma chamada sincrona ao `OrderService`, adicionando latencia e novo ponto de falha no caminho.
**Impact:** A especificacao da feature precisa validar no Tempo o encadeamento `kafka consume orders` -> `GET /orders/{id}` -> `kafka publish notifications` dentro do mesmo trace.

---

### AD-015: Falhas de enriquecimento no ProcessingWorker sao observadas e interrompem a publicacao em `notifications` (2026-03-19)

**Decision:** Quando o `GET /orders/{id}` retornar `404` ou falhar por timeout/5xx/rede, o `ProcessingWorker` nao deve publicar a mensagem seguinte no topic `notifications`; ele deve registrar log estruturado, marcar o span de consumo/processamento com erro e seguir saudavel para processar novas mensagens.
**Reason:** Nesta etapa da PoC, a prioridade e tornar a causalidade do erro visivel em traces e logs sem antecipar politicas de retry, DLQ ou persistencia compensatoria.
**Trade-off:** Mensagens podem ser descartadas ou depender da estrategia de consumo configurada sem retentativa dedicada nesta feature.
**Impact:** A spec precisa deixar claro que retry, backoff e DLQ continuam fora de escopo e que a validacao local deve cobrir tanto o caso `404` quanto falha de infraestrutura HTTP.

---

### AD-016: Reutilizar a estrategia atual de propagacao W3C no ProcessingWorker antes de extrair biblioteca compartilhada (2026-03-19)

**Decision:** A implementacao da feature `processing-worker-consumer-http-call` deve manter a mesma abordagem de `KafkaTracingHelper` ja validada no `OrderService`, com extracao de `traceparent`/`tracestate` no consume e injecao dos mesmos headers no publish para `notifications`, mesmo que o helper ainda fique local ao worker nesta etapa.
**Reason:** A baseline de M2 ja validou a injecao manual W3C no `OrderService`; repetir o mesmo contrato reduz risco e evita abrir uma refatoracao estrutural maior antes da hora.
**Trade-off:** Pode haver duplicacao temporaria do helper entre servicos ate a feature do `NotificationWorker` justificar uma extracao compartilhada com melhor custo-beneficio.
**Impact:** O design e as tasks da feature passam a assumir `Inject(Activity?, Headers)` e `Extract(Headers)` como contrato padrao para os hops Kafka da PoC.

---

### AD-017: Chamada ao OrderService no ProcessingWorker deve usar HttpClient nomeado com timeout curto e configuravel (2026-03-19)

**Decision:** O `ProcessingWorker` deve chamar `GET /orders/{id}` usando um `HttpClient` registrado em DI, com `BaseAddress` configuravel por ambiente e timeout explicito, em vez de montar requests ad hoc no loop do worker.
**Reason:** O bootstrap OTel ja instrumenta `HttpClient`, e um cliente nomeado facilita padronizacao, observabilidade e simulacao controlada de timeout sem espalhar configuracao pela logica de negocio.
**Trade-off:** Introduz uma pequena camada adicional de infraestrutura no worker, mas reduz acoplamento no `BackgroundService`.
**Impact:** A implementacao da feature precisara adicionar configuracao para `ORDER_SERVICE_BASE_URL` e `ORDER_SERVICE_TIMEOUT_SECONDS`, alem de encapsular `200`, `404` e falhas tecnicas em um cliente dedicado.

---

### AD-018: Headers Kafka ausentes ou invalidos no ProcessingWorker devem iniciar novo trace sem abortar o consumo (2026-03-19)

**Decision:** O `ProcessingWorker` deve tratar `Headers` Kafka nulos, ausentes ou com `traceparent` invalido como ausencia de contexto distribuido, iniciar um novo trace e seguir com o processamento normal.
**Reason:** A spec da feature exige continuidade observavel mesmo quando os headers W3C nao estao disponiveis, e mensagens produzidas manualmente durante validacao podem nao trazer headers.
**Trade-off:** Parte do fluxo pode perder a correlacao com o trace original quando os headers vierem ausentes ou corrompidos, mas o pipeline permanece operacional e isso fica explicito nos logs.
**Impact:** O helper `KafkaTracingHelper.Extract` passou a aceitar `Headers?`, retornando `null` com seguranca, e o loop do worker passou a registrar warning estruturado antes de continuar o fluxo.

---

### AD-019: Excecoes inesperadas no pipeline do ProcessingWorker nao devem derrubar o host (2026-03-19)

**Decision:** O loop principal do `ProcessingWorker` deve encapsular falhas inesperadas por mensagem, registrar erro e seguir consumindo as proximas mensagens.
**Reason:** A feature precisa manter o worker saudavel apos falhas de enriquecimento, payload e publicacao, inclusive quando surgir uma excecao nao prevista pela classificacao principal de erros.
**Trade-off:** Falhas pontuais ficam visiveis por log e trace, mas a mensagem especifica pode ser descartada conforme a estrategia atual de consumo sem retry dedicado.
**Impact:** O loop do worker agora captura excecoes inesperadas ao redor de `ProcessMessageAsync` e preserva a disponibilidade do processo host.

---

### AD-020: NotificationWorker deve persistir o payload de `notifications` sem alterar seus contratos externos (2026-03-19)

**Decision:** A feature `notification-worker-consumer-persistencia` vai consumir o payload minimo ja estabilizado em `notifications` e persisti-lo em tabela propria do servico, adicionando apenas metadados internos de observabilidade como `persistedAtUtc` e `traceId`.
**Reason:** O milestone M2 precisa fechar o fluxo distribuido com um resultado material no PostgreSQL sem reabrir os contratos ja consolidados entre `OrderService` e `ProcessingWorker`.
**Trade-off:** A tabela do `NotificationWorker` fica propositalmente enxuta e orientada a observabilidade, nao a um modelo de negocio final de notificacoes.
**Impact:** O design da feature deve copiar os seis campos do evento `notifications` para a persistencia, preservar o payload Kafka intacto e permitir correlacao direta entre banco e Tempo.

---

### AD-021: NotificationWorker deve classificar falhas em `consume_failed`, `invalid_payload` e `persistence_failed` sem retry dedicado (2026-03-19)

**Decision:** O `NotificationWorker` deve tratar falhas de consumo Kafka, payload invalido e persistencia no PostgreSQL como classes distintas de erro observavel, mantendo o host saudavel e sem introduzir retry, DLQ ou outbox nesta etapa.
**Reason:** A feature precisa demonstrar claramente onde o ultimo hop do pipeline parou, sem aumentar o escopo com politicas de recuperacao fora do milestone.
**Trade-off:** Mensagens problematicas permanecem sem estrategia automatica de reprocessamento nesta fase da PoC.
**Impact:** O spec, o design e as tasks da feature passam a exigir logs estruturados, spans com erro e ausencia de encerramento do host para cada uma dessas classes.

---

### AD-022: NotificationWorker persistira o ultimo hop em `notification_results` com `traceId` vindo do span de consumo (2026-03-19)

**Decision:** A implementacao da feature `notification-worker-consumer-persistencia` deve gravar uma entidade interna `PersistedNotification` em tabela propria `notification_results`, copiando os seis campos do payload de `notifications` e adicionando apenas `persistedAtUtc` e `traceId` obtido do contexto corrente do span `kafka consume notifications`.
**Reason:** O milestone precisa de um artefato persistido minimo e correlacionavel com o Tempo sem alterar o contrato externo do evento Kafka nem misturar esse resultado com a tabela `orders` do `OrderService`.
**Trade-off:** O modelo fica orientado a observabilidade e demonstracao, nao a um dominio final de notificacoes; tambem nao antecipa idempotencia forte nem registro persistido das falhas.
**Impact:** O design e as tasks agora assumem `NotificationDbContext`, tabela `notification_results`, indices por `order_id` e `trace_id` e correlacao primaria via `TraceId` persistido.

---

### AD-023: Headers W3C ausentes ou invalidos no NotificationWorker devem iniciar novo trace e ainda permitir persistencia correlacionavel (2026-03-19)

**Decision:** O `NotificationWorker` deve tratar `traceparent` ausente ou invalido como ausencia de contexto distribuido, iniciar um novo trace local para o span `kafka consume notifications`, registrar warning estruturado e persistir o `traceId` desse novo contexto quando o payload for valido.
**Reason:** A feature precisa continuar operacional e observavel mesmo quando o hop Kafka chegar sem contexto distribuido utilizavel, preservando a correlacao entre banco, log e Tempo dentro do processamento local.
**Trade-off:** A cadeia ponta a ponta com `OrderService` e `ProcessingWorker` deixa de ser continua nesses casos, mas o comportamento fica explicito e diagnostico.
**Impact:** A validacao da feature passa a distinguir claramente entre o caminho feliz com `TraceId` unico entre os tres servicos e o caminho degradado com novo trace iniciado apenas no `NotificationWorker`.

---

### AD-024: Bootstrap da tabela `notification_results` deve usar DDL explicito em banco compartilhado (2026-03-19)

**Decision:** O `NotificationWorker` deve criar `notification_results` e seus indices com `CREATE TABLE IF NOT EXISTS` e `CREATE INDEX IF NOT EXISTS` no startup, em vez de depender de `EnsureCreatedAsync()`.
**Reason:** O PostgreSQL da PoC ja contem a tabela `orders` de outro `DbContext`; nesse cenario, `EnsureCreatedAsync()` nao cria automaticamente as tabelas do `NotificationWorker`, o que levou a `42P01 relation does not exist` no primeiro teste integrado.
**Trade-off:** O bootstrap fica manual e especifico do servico, em vez de reaproveitar a API generica de criacao de schema do EF Core.
**Impact:** O startup do `NotificationWorker` agora e deterministico em banco compartilhado e permite subir a feature em ambiente limpo ou reaproveitado sem DDL manual.

---

### AD-025: `consume_failed` no NotificationWorker deve vir tambem do error handler do consumer Kafka (2026-03-19)

**Decision:** O registro do `IConsumer<string,string>` no `NotificationWorker` deve usar `SetErrorHandler(...)` para emitir logs estruturados `Classification=consume_failed` em indisponibilidade de broker, mesmo quando `Consume()` nao lanca excecao imediatamente.
**Reason:** Na validacao integrada, parar o Kafka produziu apenas logs internos do librdkafka; sem um error handler explicito, a classificacao funcional exigida pela spec nao aparecia nos logs da aplicacao.
**Trade-off:** Eventos de erro de broker podem gerar mais de um log `consume_failed` por periodo de indisponibilidade, mas isso e preferivel a perder observabilidade da classe de erro.
**Impact:** O cenario `consume_failed` passou a ser validavel por logs da propria aplicacao, mantendo o host saudavel e coerente com a taxonomia da feature.

---

### AD-026: Consolidar a propagacao W3C Kafka em nucleo compartilhado com fachadas locais minimas (2026-03-19)

**Decision:** A feature `propagacao-trace-context-kafka` deve extrair a logica W3C relevante para um artefato compartilhado sem dependencia direta de Kafka e manter `KafkaTracingHelper` como fachada local minima por servico durante a migracao.
**Reason:** Essa abordagem reduz a duplicacao funcional sem forcar alinhamento imediato de versoes de `Confluent.Kafka`, sem abrir churn amplo em namespaces e sem tocar desnecessariamente nos call sites ja validados em M2.
**Trade-off:** Ainda havera um arquivo `KafkaTracingHelper` por servico na primeira iteracao da consolidacao, mas ele passara a ser apenas compatibilidade de borda, nao nova fonte de logica duplicada.
**Impact:** A proxima implementacao deve introduzir um nucleo compartilhado pequeno, adaptar os tres helpers locais por delegacao e revalidar o fluxo no Tempo sem alterar spans, topicos ou payloads.

---

### AD-027: O contrato minimo compartilhado deve ser uniforme mesmo quando um servico nao usa todo o helper hoje (2026-03-19)

**Decision:** Os tres servicos devem convergir para o mesmo contrato Kafka-facing `Extract(Headers?)` e `Inject(Activity?, Headers)`, mesmo que o `NotificationWorker` hoje so consuma e o `OrderService` hoje so publique.
**Reason:** A divergencia atual de assinaturas aumenta a chance de drift futuro e complica a consolidacao do helper compartilhado.
**Trade-off:** Alguns metodos existirao como contrato de consistencia sem uso imediato no fluxo atual.
**Impact:** A implementacao deve adicionar `Inject(...)` ao `NotificationWorker`, alinhar `Extract(...)` do `OrderService` para `Headers?` e manter comportamento no-op seguro para `activity = null` e `headers = null`.

---

### AD-028: Compartilhar o nucleo W3C por arquivo linkado e copiar `src/Shared` nos Dockerfiles (2026-03-19)

**Decision:** Implementar o helper compartilhado como arquivo puro `src/Shared/W3CTraceContext.cs`, incluido por `Compile Include` nos tres `.csproj`, e copiar `src/Shared/` nos tres Dockerfiles durante o build das imagens.
**Reason:** Essa abordagem preserva a ausencia de dependencia direta de Kafka no nucleo compartilhado, evita introduzir um quarto projeto na solution e mantem a refatoracao compativel com o build em container ja validado para a PoC.
**Trade-off:** O compartilhamento fica baseado em inclusao de arquivo, nao em `ProjectReference`; isso e suficiente para a iteracao atual, mas ainda nao representa uma biblioteca reutilizavel fora da solution.
**Impact:** Os tres `KafkaTracingHelper` passaram a delegar parse e injecao W3C ao mesmo nucleo, e os Dockerfiles precisaram copiar explicitamente `src/Shared/` para que `dotnet publish` em container continuasse funcionando.

---

### AD-029: Metricas customizadas devem reutilizar o pipeline OTLP existente, sem alterar o collector base (2026-03-19)

**Decision:** A feature `metricas-customizadas` deve adicionar apenas bootstrap de meters e instrumentos customizados nos tres servicos, reutilizando `OTEL_EXPORTER_OTLP_ENDPOINT=http://otelcol:4317` e a pipeline `metrics` ja existente no `otelcol.yaml`, sem criar exporter paralelo, endpoint Prometheus proprio nos servicos ou mudancas estruturais no compose.
**Reason:** O backend LGTM e o collector ja recebem metricas por OTLP e fazem exportacao para `lgtm:4318/v1/metrics`; reusar esse caminho reduz risco e mantem a feature focada em instrumentacao de aplicacao.
**Trade-off:** A validacao continua dependente do ambiente Docker e da saude do collector/LGTM, em vez de uma exposicao local isolada por servico.
**Impact:** A implementacao deve estender `AddOtelInstrumentation()` com `WithMetrics(...)`, registrar explicitamente os meters customizados e validar a chegada das series pelo caminho servico -> otelcol -> LGTM, sem reabrir a infra base de observabilidade.

---

### AD-030: Nomes e tags de metricas devem permanecer de baixa cardinalidade e sem IDs de negocio (2026-03-19)

**Decision:** A feature `metricas-customizadas` deve limitar tags customizadas a eixos estaveis e de baixa cardinalidade, como `result`, `status`, `topic` e `consumer_group`, proibindo `orderId`, `traceId`, `description`, mensagens de excecao e qualquer valor dinamico por evento.
**Reason:** A PoC precisa demonstrar metricas uteis sem introduzir explosao de series nem custo desproporcional no backend.
**Trade-off:** Parte do diagnostico fino continua concentrada em traces e logs, nao em metricas.
**Impact:** O catalogo minimo da feature passa a usar counters por resultado agregado, histograms por resultado do fluxo e gauges agregados por backlog/lag; qualquer validacao no backend deve conferir tambem a ausencia de labels de alta cardinalidade.

---

### AD-031: Gauges de backlog e lag devem usar agregacao minima e callback seguro (2026-03-19)

**Decision:** O `OrderService` deve expor backlog apenas para os estados `pending_publish` e `publish_failed`, e os dois workers devem expor `kafka.consumer.lag` agregado por `topic` e `consumer_group`, evitando dimensoes por `orderId` e, por padrao, evitando series por particao nesta feature.
**Reason:** Isso cobre os sinais operacionais minimos de fila e atraso sem introduzir consultas ou chamadas de broker em excesso por serie.
**Trade-off:** A visibilidade por particao fica fora desta iteracao e pode ser acrescentada depois somente se o dashboard de M3 realmente exigir.
**Impact:** A implementacao deve preferir gauges observaveis com estado agregado e callbacks leves; se um gauge depender de consulta a banco ou broker, essa leitura precisa ser bounded, tolerar falha e nunca quebrar o fluxo principal dos servicos.

---

### AD-032: Cada servico tera recorder proprio de metricas, sem biblioteca compartilhada nesta iteracao (2026-03-19)

**Decision:** A implementacao de `metricas-customizadas` deve criar um recorder/coordenador de metricas por servico, com `Meter` e instrumentos proprios de `OrderService`, `ProcessingWorker` e `NotificationWorker`, em vez de introduzir uma biblioteca compartilhada de metrica nesta fase.
**Reason:** Os tres servicos compartilham o pipeline OTLP, mas nao compartilham os mesmos resultados, pontos de emissao, dependencias de banco/Kafka nem a mesma versao de `Confluent.Kafka`; abstrair cedo demais aumentaria o acoplamento sem reduzir complexidade real.
**Trade-off:** Havera padrao repetido entre servicos, mas a repeticao sera estruturalmente pequena e alinhada ao comportamento especifico de cada fluxo.
**Impact:** O design passa a assumir classes locais como `OrderMetrics`, `ProcessingMetrics` e `NotificationMetrics`, com contratos pequenos e nomes de instrumentos centralizados por servico.

---

### AD-033: ObservableGauges devem ler snapshots em memoria atualizados fora do callback (2026-03-19)

**Decision:** Os gauges `orders.backlog.current` e `kafka.consumer.lag` devem expor apenas snapshots em memoria; a leitura de banco ou broker deve acontecer antes, por sampler/refresh bounded, nunca diretamente no callback do `ObservableGauge`.
**Reason:** Callbacks de metricas precisam permanecer baratos, deterministicos e seguros para nao introduzir bloqueios ou falhas no pipeline de exportacao.
**Trade-off:** Os gauges passam a ter leve defasagem temporal em relacao ao estado real, controlada pelo intervalo de refresh.
**Impact:** A implementacao deve introduzir estado singleton de snapshot e um mecanismo explicito de refresh: sampler periodico para backlog do `OrderService` e refresh throttled ou sampler dedicado para lag nos workers.

---

### AD-034: Nomes de instrumentos permanecem em dot-notation no codigo, mas a validacao deve aceitar normalizacao Prometheus (2026-03-19)

**Decision:** Os instrumentos serao nomeados no codigo exatamente como definidos na spec, usando dot-notation, mas a validacao no backend deve considerar a eventual normalizacao para nomes compativeis com Prometheus, como underscores no Explore.
**Reason:** O catalogo da feature e expresso em nomenclatura OpenTelemetry, enquanto o backend LGTM/Prometheus pode apresentar os nomes normalizados na camada de consulta.
**Trade-off:** A verificacao manual precisa conhecer as duas formas do nome, aumentando um pouco a carga cognitiva da validacao.
**Impact:** O design e as futuras tasks devem distinguir claramente entre nome canonico do instrumento no codigo e nome esperado nas queries do backend quando houver normalizacao.

---

### AD-035: Lag Kafka dos workers sera atualizado no proprio thread de consumo usando snapshots locais (2026-03-19)

**Decision:** A implementacao de `kafka.consumer.lag` nos workers usa um snapshot singleton atualizado logo apos `Consume()` com base nas particoes atualmente atribuidas ao mesmo `IConsumer`, em vez de criar um segundo consumer ou um poller paralelo so para lag.
**Reason:** Um segundo consumer no mesmo `consumer_group` distorceria a atribuicao e aumentaria o risco funcional da baseline de M2; reaproveitar o consumer ja existente preserva o fluxo e mantem o callback do gauge livre de IO.
**Trade-off:** O lag agregado reflete as particoes atribuídas ao worker no momento do consumo e pode ficar levemente defasado entre polls, o que e aceitavel para a PoC.
**Impact:** `ProcessingLagRefresher` e `NotificationLagRefresher` passaram a atuar como mecanismo leve de refresh acionado pelo loop do worker, mantendo `ObservableGauge` apenas como leitor do ultimo snapshot conhecido.

---

### AD-036: Histograms customizados aparecem normalizados no backend com sufixo de unidade em millisecondos (2026-03-19)

**Decision:** A validacao da feature deve considerar os histograms do backend LGTM/Prometheus nas formas `orders_create_duration_milliseconds_*`, `orders_processing_duration_milliseconds_*` e `notifications_persistence_duration_milliseconds_*`, e nao apenas nas raizes sem unidade.
**Reason:** Na implementacao, os instrumentos usam unidade `ms` conforme a spec; o exporter/bridge Prometheus do LGTM normalizou os nomes adicionando o sufixo `_milliseconds` e as series `_bucket`, `_count` e `_sum`.
**Trade-off:** A checagem manual no Explore e na API do Prometheus precisa conhecer essa normalizacao especifica para histograms, enquanto counters e gauges continuam com nomes mais proximos do catalogo original.
**Impact:** As evidencias de validacao da feature e os proximos artefatos de dashboard devem consultar explicitamente `orders_created_total`, `orders_backlog_current`, `orders_processed_total`, `notifications_persisted_total`, `kafka_consumer_lag` e os histograms na forma normalizada com `_milliseconds_*`.

---

### AD-037: Dashboard Grafana deve reutilizar apenas as series normalizadas ja validadas, sem criar novos sinais (2026-03-19)

**Decision:** A feature `dashboard-grafana` deve consultar exclusivamente as series ja validadas em `metricas-customizadas` no backend LGTM/Prometheus, usando as formas normalizadas `orders_created_total`, `orders_backlog_current`, `orders_processed_total`, `notifications_persisted_total`, `kafka_consumer_lag` e os histograms `*_duration_milliseconds_*`, sem introduzir novas metricas, labels customizadas ou transformacoes que exijam mudanca nos servicos.
**Reason:** O objetivo de M3 nesta iteracao e visualizacao reproduzivel dos sinais ja estabilizados, nao reabrir o catalogo de metricas nem a baseline funcional de M2/M3.
**Trade-off:** O dashboard fica limitado ao recorte atual de throughput, latencia, backlog e lag; sinais adicionais, erros derivados e correlacoes avancadas ficam para iteracoes futuras se realmente necessarios.
**Impact:** O spec, o design e a implementacao do dashboard devem partir diretamente das queries PromQL dessas series normalizadas e tratar qualquer ausencia de dado como problema de visualizacao/provisionamento, nao de instrumentacao nova.

---

### AD-038: Dashboard Grafana deve bindar no datasource provisionado `uid: prometheus` do LGTM atual (2026-03-19)

**Decision:** A feature `dashboard-grafana` deve referenciar explicitamente o datasource Prometheus provisionado na imagem `grafana/otel-lgtm`, usando `uid: prometheus` no JSON do dashboard, sem criar datasource novo na PoC.
**Reason:** A inspecao do container `lgtm` confirmou que o datasource existente esta em `/otel-lgtm/grafana/conf/provisioning/datasources/grafana-datasources.yaml` com `name: Prometheus` e `uid: prometheus`; reaproveita-lo reduz risco de paineis vazios e evita churn desnecessario.
**Trade-off:** O dashboard fica acoplado ao UID provisionado pela imagem atual, o que exige revalidacao rapida se a imagem base mudar no futuro.
**Impact:** O design e as proximas tasks devem tratar o binding por UID como criterio obrigatorio de implementacao e validacao do dashboard.

---

### AD-040: O provisionamento da PoC deve usar caminhos reais do LGTM e provider proprio isolado (2026-03-19)

**Decision:** A implementacao de `dashboard-grafana` deve montar um provider YAML proprio da PoC em `/otel-lgtm/grafana/conf/provisioning/dashboards/` e os dashboards JSON da PoC em `/otel-lgtm/dashboards/`, sem sobrescrever o provider nativo `grafana-dashboards.yaml` da imagem.
**Reason:** A inspecao do runtime mostrou que a imagem usa `/otel-lgtm/grafana/conf/provisioning` como raiz de provisioning e ja carrega dashboards nativos por arquivo; isolar a PoC evita colisao com os dashboards embarcados do LGTM.
**Trade-off:** A implementacao passa a manter dois artefatos versionados de Grafana no repositorio em vez de um unico arquivo JSON solto.
**Impact:** `docker-compose.yaml` precisara apenas de mounts read-only adicionais no servico `lgtm`, enquanto os servicos .NET e o collector permanecem intactos.

---

### AD-041: Alertas Grafana devem reutilizar o datasource `uid: prometheus` e permanecer no plano de configuracao (2026-03-19)

**Decision:** A feature `alertas-grafana` deve partir exclusivamente das series ja validadas em `dashboard-grafana` e `metricas-customizadas`, reutilizando o datasource provisionado `uid: prometheus` e limitando o diff funcional a regras, contact points e politicas de alerta do Grafana.
**Reason:** O objetivo e fechar M3 com avaliacao automatizada sobre sinais ja existentes, sem reabrir instrumentacao, collector ou contratos funcionais da PoC.
**Trade-off:** A feature fica deliberadamente dependente da baseline visual e da normalizacao Prometheus ja confirmadas; qualquer lacuna de dados deve ser tratada como problema de configuracao/consulta, nao como gatilho para novas metricas.
**Impact:** O design e a implementacao devem preservar metricas, labels, `otelcol.yaml`, processors, pipelines OTLP, payloads Kafka, persistencia e servicos .NET exatamente como estao hoje.

---

### AD-042: O contact point de alertas da PoC deve ser local e sem canais externos reais (2026-03-19)

**Decision:** A feature `alertas-grafana` deve usar um contact point local e reproduzivel, priorizando `webhook` mock simples e aceitando fallback para destino local baseado em log, sem Slack, email ou qualquer integracao externa real.
**Reason:** A PoC precisa demonstrar o fluxo de alerta fim a fim sem depender de credenciais, servicos de terceiros ou ruído operacional fora do ambiente local.
**Trade-off:** A validacao passa a depender de um helper local adicional no compose, mas o helper permanece totalmente fora do fluxo de telemetria dos servicos de negocio.
**Impact:** O diff funcional da feature pode ficar restrito a `docker-compose.yaml`, aos artefatos versionados de alerting e ao helper local do webhook mock.

---

### AD-043: O schema final das alert rules deve seguir o runtime do Grafana 12.4.1, nao o schema generico do editor YAML (2026-03-19)

**Decision:** A implementacao de `alertas-grafana` deve tratar o `sample.yaml` do diretorio real `/otel-lgtm/grafana/conf/provisioning/alerting` e a API de provisioning do Grafana como fonte de verdade para `apiVersion`, `groups`, `contactPoints`, `policies`, `execErrState` e para o shape de `data.model` com Prometheus + `__expr__` (`reduce` e `threshold`).
**Reason:** O runtime real do Grafana 12.4.1 aceitou os arquivos provisionados e a API retornou os recursos com `provenance: file`, enquanto a validacao generica do editor apontou falsos positivos como `Missing property file_format`, que nao pertencem ao schema de unified alerting do Grafana.
**Trade-off:** A validacao local precisa privilegiar runtime/API do Grafana sobre mensagens genericas do editor quando houver conflito de schema.
**Impact:** O processo de implementacao e manutencao dessa feature passa a exigir verificacao em runtime do `lgtm` e da API de provisioning antes de considerar um problema de schema como real.

---

### AD-044: A validacao acelerada da latencia pode parar em firing real sem aguardar resolved quando o objetivo for velocidade (2026-03-19)

**Decision:** Para a feature `alertas-grafana`, a validacao acelerada escolhida pelo usuario pode encerrar o alerta de latencia apos comprovar requests acima de 500 ms, regra em `Firing` na API e payload `firing` entregue ao webhook mock, sem aguardar explicitamente o `Resolved` desse segundo alerta.
**Reason:** O alerta de latencia usa janela PromQL de 5 minutos com `for: 1m`, o que torna o tempo total de prova muito maior do que o do alerta de lag; a feature ja tinha um ciclo completo `Pending` -> `Firing` -> `Resolved` validado no alerta de lag.
**Trade-off:** O segundo alerta fica validado por evidencia forte de runtime e entrega real no receiver, mas nao por ciclo completo ate `Resolved` nesta execucao acelerada.
**Impact:** O fechamento da feature continua confiavel para a PoC local, reduz o tempo de execucao e mantem a prova mais cara restrita a um unico alerta obrigatorio.
**Trade-off:** O valor demonstrado fica concentrado na corretude do disparo e do payload local, nao na integracao com canais corporativos reais.
**Impact:** As proximas etapas devem focar provisioning de receiver local, policy e evidencias de `firing/resolved`, mantendo o escopo em configuracao de Grafana e helper local quando necessario.

---

### AD-043: Unified alerting da PoC deve usar `/otel-lgtm/grafana/conf/provisioning/alerting` com policy minima de dono unico (2026-03-19)

**Decision:** A implementacao de `alertas-grafana` deve montar artefatos versionados diretamente em `/otel-lgtm/grafana/conf/provisioning/alerting`, separando `groups`, `contactPoints` e `policies` em arquivos distintos, mas mantendo a policy tree inteira sob um unico arquivo da PoC.
**Reason:** A inspecao do runtime no Grafana `12.4.1` confirmou a existencia do diretorio nativo de alerting e do `sample.yaml` com suporte a `groups`, `contactPoints` e `policies`; ao mesmo tempo, a documentacao oficial reforca que a notification policy tree e um recurso unico e sobrescrito integralmente no provisioning.
**Trade-off:** O design adiciona tres mounts read-only ao `lgtm` e exige mais disciplina de ownership dos artefatos de alerting, mas reduz o risco de sobrescrever partes erradas da configuracao futura.
**Impact:** A proxima iteracao deve gerar `otel-poc-alert-rules.yaml`, `otel-poc-contact-points.yaml` e `otel-poc-notification-policies.yaml`, validar esses caminhos no container e tratar qualquer helper de webhook mock apenas como detalhe de ambiente.

---

### AD-039: Escopo visual minimo do dashboard deve ser organizado por servico e separado da feature de alertas (2026-03-19)

**Decision:** O dashboard minimo de M3 deve ser organizado por secoes de `OrderService`, `ProcessingWorker` e `NotificationWorker`, com paineis de throughput/latencia e backlog/lag para cada servico quando aplicavel, e sem incluir regras de alerta, contact points, notificacoes ou estados derivados da feature posterior `alertas-grafana`.
**Reason:** Essa separacao reduz ambiguidade de milestone, limita churn no JSON provisionado e facilita validar primeiro a camada de visualizacao antes da camada de avaliacao/alerta.
**Trade-off:** O milestone continua dependente de uma feature posterior para completar a parte de alertas prevista no roadmap de alto nivel.
**Impact:** O design da feature deve focar em layout, queries e provisionamento de dashboard; qualquer necessidade de thresholds ou regras automatizadas deve ser registrada como fora de escopo nesta etapa.

---

## Active Blockers

### B-002: Host local sem .NET 10 SDK

**Discovered:** 2026-03-19
**Impact:** Impede validar `dotnet build otel-poc.sln` diretamente no host Windows, apesar de a solution compilar com SDK 10 em container Docker.
**Workaround:** Usar `docker run --rm -v "${PWD}:/src" -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet build otel-poc.sln`.
**Resolution:** Instalar .NET 10 SDK no host ou atualizar o ambiente base de desenvolvimento.

---

## Regression Risks

### R-007: Documentacao pode ficar enganosa se misturar endpoint interno do webhook mock com URLs locais da demo (2026-03-19)

**Discovered:** 2026-03-19
**Impact:** Se o futuro `README.md` tratar o `alert-webhook-mock` como se estivesse publicado no host, a pessoa usuaria vai tentar validar alertas por uma URL `localhost` inexistente, comprometendo a reproducao da demo mesmo com a stack correta.
**Mitigation:** Na fase de design e implementacao do README, manter uma tabela unica separando portas expostas no host de servicos internos do compose e usar `docker compose logs alert-webhook-mock` como caminho padrao de verificacao do receiver local.

---

### R-001: Convergencia involuntaria de dependencia Kafka durante a consolidacao

**Discovered:** 2026-03-19
**Impact:** Transformar o helper compartilhado em um projeto diretamente dependente de `Confluent.Kafka` pode forcar alinhamento de versoes entre `OrderService` e workers antes da hora, ampliando o risco da refatoracao alem da propagacao W3C.
**Mitigation:** Preferir um nucleo compartilhado puro, sem dependencia direta de Kafka, e deixar a adaptacao de `Headers` em fachadas locais minimas.

---

### R-002: Drift de spans, headers ou logs por tocar arquivos de negocio alem do helper

**Discovered:** 2026-03-19
**Impact:** Mudancas desnecessarias em publishers e workers podem alterar o comportamento observavel ja validado no Tempo, mesmo que o objetivo da feature seja apenas consolidar o helper.
**Mitigation:** Limitar a migracao a um artefato compartilhado e a adaptacoes locais finas, preservando integralmente nomes de spans, topicos, payloads e semantica de `traceparent`/`tracestate`.

---

### R-003: Regressao visual por layout confuso ou escolhas de painel inconsistentes no dashboard (2026-03-19)

**Discovered:** 2026-03-19
**Impact:** Mesmo com queries corretas, um dashboard com paineis mal organizados, tipos visuais inadequados ou seccoes misturadas pode reduzir a legibilidade da demo e gerar falsa percepcao de regressao funcional.
**Mitigation:** Manter layout minimo organizado por servico, separar throughput/latencia de backlog/lag e validar a leitura do dashboard provisionado em ambiente limpo antes de considerar a feature concluida.

---

### R-004: Divergencia entre normalizacao esperada e series reais do backend pode zerar paineis de histogram (2026-03-19)

**Discovered:** 2026-03-19
**Impact:** Se o dashboard usar nomes ou labels diferentes dos observados no Explore, especialmente nos histograms `_milliseconds_bucket`, os paineis de percentil podem ficar vazios apesar de a coleta estar saudavel.
**Mitigation:** Na proxima iteracao, reconfirmar no Explore/Prometheus a forma final das series e dos labels usados nas queries antes de fechar o JSON provisionado.

---

### R-006: O caminho real de provisioning de alerting no LGTM pode divergir do caminho de dashboards (2026-03-19)

**Discovered:** 2026-03-19
**Impact:** A imagem `grafana/otel-lgtm:latest` ja teve caminhos internos diferentes do Grafana padrao para dashboards; se o provisioning de alerting usar outra raiz ou convencao, a implementacao pode parecer correta no repositorio e ainda assim nao carregar regras/contact points no runtime.
**Mitigation:** Na fase de design e implementacao, inspecionar explicitamente o container `lgtm` para confirmar a raiz efetiva de provisioning de alerting antes de fixar mounts e arquivos versionados.

---

### R-005: Validacao funcional do dashboard depende da saude momentanea do fluxo da PoC, nao apenas do provisioning (2026-03-19)

**Discovered:** 2026-03-19
**Impact:** Durante a validacao de `dashboard-grafana`, um `POST /orders` de aquecimento retornou `503` e os logs do `OrderService` mostraram timeouts transitorios de publish no Kafka e de conexao com PostgreSQL; isso pode levar queries de throughput/latencia a retornarem `0` ou `NaN` mesmo com dashboard, provider e datasource corretamente provisionados.
**Mitigation:** Separar a validacao em duas camadas: primeiro confirmar provisioning, folder, dashboard e datasource via API/runtime do Grafana; depois confrontar as queries PromQL no backend, aceitando `0` ou `NaN` como estado operacional da carga quando o ambiente estiver instavel, sem tratar isso como regressao do dashboard por si so.

---

## Resolved Blockers

### B-008: Nome ou UID do datasource Prometheus do Grafana ainda nao estava validado para os artefatos versionados

**Discovered:** 2026-03-19
**Impact:** Um dashboard provisionado poderia carregar com paineis vazios ou datasource nao encontrado se o JSON referenciasse um identificador diferente do datasource real do stack LGTM.
**Workaround:** Inspecionar o container `lgtm` antes de fechar o design da feature.
**Resolution:** Resolvido em 2026-03-19 com a validacao do datasource provisionado em `/otel-lgtm/grafana/conf/provisioning/datasources/grafana-datasources.yaml`, confirmando `name: Prometheus`, `uid: prometheus` e o caminho real de provisioning em `/otel-lgtm/grafana/conf/provisioning`.

---

### B-007: Build das imagens falhava apos extrair o helper compartilhado para `src/Shared`

**Discovered:** 2026-03-19
**Impact:** O build da solution em container SDK 10 passava, mas `docker compose up -d --build` quebrava no `dotnet publish` porque os Dockerfiles copiavam apenas a pasta do servico e nao o arquivo compartilhado linkado.
**Workaround:** Nenhum definitivo sem ajustar o contexto copiado para o estagio de build.
**Resolution:** Resolvido em 2026-03-19 adicionando `COPY ["src/Shared/", "src/Shared/"]` aos Dockerfiles de `OrderService`, `ProcessingWorker` e `NotificationWorker`.

---

### B-006: `EnsureCreatedAsync()` nao criava a tabela do NotificationWorker em banco compartilhado

**Discovered:** 2026-03-19
**Impact:** O `notification-worker` processava mensagens validas, mas todas terminavam em `persistence_failed` com `42P01 relation "notification_results" does not exist`, bloqueando o caminho feliz de M2.
**Workaround:** Nenhum definitivo sem criar explicitamente a tabela do servico.
**Resolution:** Resolvido em 2026-03-19 trocando o bootstrap por DDL explicito `CREATE TABLE IF NOT EXISTS notification_results` e indices `IF NOT EXISTS` no startup do `NotificationWorker`.

---

### B-005: Bootstrap do schema PostgreSQL ainda não existe em código

**Discovered:** 2026-03-19
**Impact:** Impedia que `POST /orders` e `GET /orders/{id}` fossem reproduzíveis em compose limpo sem DDL manual.
**Workaround:** Nenhum definitivo sem bootstrap automático no startup.
**Resolution:** Resolvido em 2026-03-19 com `EnsureCreatedAsync()` no startup do `OrderService`, criando a tabela `orders` em ambiente limpo e mantendo logs claros em caso de falha de conexão.

---

### B-001: Serviços .NET ausentes para o compose

**Discovered:** 2026-03-19
**Impact:** Impedia concluir T5-T11 da feature `docker-compose-infra` e validar o ambiente completo de M1.
**Workaround:** Manter apenas a infraestrutura de terceiros no compose até a feature `.NET Solution` criar solution, projetos e Dockerfiles.
**Resolution:** Resolvido em 2026-03-19 com a criação de `otel-poc.sln`, `src/OrderService`, `src/ProcessingWorker`, `src/NotificationWorker` e seus Dockerfiles.

---

### B-004: OTel Collector não iniciava com configuração legado (2026-03-19)

**Discovered:** 2026-03-19
**Impact:** Bloqueava qualquer exportação OTLP e invalidava a validação da feature `otel-bootstrap`.
**Workaround:** Nenhum seguro sem corrigir `otelcol.yaml`.
**Resolution:** Resolvido em 2026-03-19 com a remoção da chave `file_format` de `otelcol.yaml`.

---

### B-003: Workers sem atividade instrumentada em M1

**Discovered:** 2026-03-19
**Impact:** Impedia comprovar no Tempo os `service.name` `processing-worker` e `notification-worker`, apesar da configuração OpenTelemetry já estar aplicada nos dois projetos.
**Workaround:** Adicionar `ActivitySource` manual temporário nos heartbeats dos workers.
**Resolution:** Resolvido em 2026-03-19 com spans manuais de heartbeat e validação positiva no Tempo para os dois workers.

---

## Lessons Learned

### L-001: Typo em keep-errors.yaml (2026-03-19)

**Context:** Durante mapeamento brownfield, o arquivo `processors/sampling/keep-errors.yaml` foi analisado.
**Problem:** Campo `name` contém o valor `keep-erros` (falta o `r` final em `errors`). Não causa erro funcional, mas é enganoso na leitura dos logs do collector.
**Solution:** Corrigir para `keep-errors` quando iniciar o desenvolvimento.
**Prevents:** Confusão ao buscar a política nos logs do collector por nome.

---

### L-002: Imagem base ASP.NET injeta HTTP_PORTS e gera aviso benigno no OrderService (2026-03-19)

**Context:** Após `docker compose up -d`, o `order-service` iniciou normalmente, mas registrou um aviso do ASP.NET Core sobre `HTTP_PORTS` ser sobrescrito por `URLS`.
**Problem:** O runtime padrão da imagem `aspnet:10.0` define `HTTP_PORTS=8080`, enquanto o serviço também chama `UseUrls("http://0.0.0.0:8080")`.
**Solution:** Manter o comportamento atual por não haver impacto funcional; considerar alinhar essa configuração numa iteração futura para reduzir ruído nos logs.
**Prevents:** Evita tratar um aviso benigno como falha funcional durante os smoke tests do compose.

---

### L-003: Validar HTTP 5xx por container dedicado na rede do compose e mais confiavel que listener temporario no host (2026-03-19)

**Context:** Durante a validacao da feature `processing-worker-consumer-http-call`, foi necessario provar o comportamento do worker diante de respostas `5xx` reais.
**Problem:** Um `HttpListener` temporario no host Windows introduziu friccao de ACL e mismatch de `Host`, levando a `400` em vez do `500` esperado quando acessado do container.
**Solution:** Usar um container efemero na rede `otel-demo` que responda `500` em qualquer rota, apontando um `ProcessingWorker` temporario para ele.
**Prevents:** Reduz falsos negativos na validacao de erros tecnicos HTTP e evita depender de particularidades de rede do host Windows.

---

### L-004: `EnsureCreatedAsync()` nao e suficiente quando multiplos DbContexts compartilham o mesmo banco da PoC (2026-03-19)

**Context:** Durante a implementacao de `notification-worker-consumer-persistencia`, o PostgreSQL ja continha a tabela `orders` criada pelo `OrderService`.
**Problem:** `EnsureCreatedAsync()` nao criou `notification_results`, levando a falhas `42P01 relation does not exist` apenas no momento da persistencia do `NotificationWorker`.
**Solution:** Para a PoC atual, usar bootstrap DDL explicito por servico quando novos DbContexts compartilharem o mesmo banco existente.
**Prevents:** Evita confiar em `EnsureCreatedAsync()` para novos contextos em banco ja inicializado por outro servico.

---

## Validation Snapshot

- `docker compose config` validou o compose consolidado com os 3 serviços .NET.
- `docker compose build order-service processing-worker notification-worker` passou.
- `docker compose up -d` subiu o ambiente completo; `docker compose ps` mostrou Kafka/Zookeeper/Postgres healthy e os 3 serviços .NET em `Up`.
- Logs iniciais de `order-service`, `processing-worker` e `notification-worker` não mostraram exceptions fatais; apenas um warning benigno de `HTTP_PORTS` no `order-service`.
- `docker run --rm -v "${PWD}:/src" -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet build otel-poc.sln` passou após a implementação do bootstrap OTel.
- `docker compose up -d --build` recompilou os 3 serviços com a configuração OTel aplicada.
- A API do Grafana listou o datasource `Tempo` e a busca em `service.name=order-service` retornou trace recente de `GET /`.
- A inspecao do container `lgtm` confirmou que o Grafana usa `/otel-lgtm/grafana/conf/provisioning` como raiz de provisioning, carrega o datasource Prometheus existente com `uid: prometheus` e mantem dashboards nativos em provider separado.
- A busca em `service.name=order-service url.path=/health` retornou 0 traces recentes após ajustar `drop-health-checks.yaml` para descarte total.
- Após adicionar `ActivitySource` manual nos workers e reconstruir os containers, as buscas em `service.name=processing-worker` e `service.name=notification-worker` passaram a retornar traces recentes de heartbeat no Tempo.
- `docker run --rm -v "${PWD}:/src" -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet build src/OrderService/OrderService.csproj` passou após adicionar EF Core PostgreSQL, Kafka e a instrumentação de banco.
- `docker compose up -d --build otelcol kafka postgres order-service` passou com o `OrderService` recriado e o schema `orders` bootstrapado automaticamente.
- `POST /orders` retornou `201 Created` com `Location: /orders/{id}` e payload persistido com `status = published`.
- `GET /orders/{id}` retornou `200 OK` refletindo exatamente o registro salvo no PostgreSQL.
- A consulta `psql` na tabela `orders` confirmou os estados `published` e `publish_failed` nos cenários saudável e de falha.
- `kafka-console-consumer` capturou mensagem do topic `orders` com `traceparent` no header e `orderId` coerente com a resposta HTTP.
- A consulta ao Tempo pelo trace id `d589f83cab3da9c1b639b2570b8fac39` confirmou o span root `POST /orders`, dois spans de cliente PostgreSQL (`INSERT` e `UPDATE`) e o span manual `kafka publish orders` no mesmo trace.
- Com o Kafka parado, `POST /orders` retornou `503 Service Unavailable` e o pedido permaneceu salvo com `status = publish_failed`.
- `docker run --rm -v "${PWD}:/src" -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet build src/ProcessingWorker/ProcessingWorker.csproj` passou apos adicionar consumer Kafka, cliente HTTP, publisher de `notifications` e endurecimento para headers ausentes.
- `docker run --rm -v "${PWD}:/src" -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet build otel-poc.sln` continuou passando apos integrar a feature no `ProcessingWorker`.
- `docker compose up -d --build order-service processing-worker kafka postgres otelcol lgtm` passou com o worker recriado e inscrito no topic `orders`.
- Um `POST /orders` real gerou consumo no `ProcessingWorker`, chamada `GET /orders/{id}` e publish no topic `notifications` com payload enriquecido minimo e header `traceparent` preservado.
- A consulta ao Tempo pelo trace id `a012646a6f26895d50120b67b84e22d8` confirmou `POST /orders` -> `kafka publish orders` -> `kafka consume orders` -> span HTTP `GET` -> `kafka publish notifications` no mesmo trace.
- `docker run --rm -v "${PWD}:/src" -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet build src/NotificationWorker/NotificationWorker.csproj` passou apos adicionar consumer Kafka, EF Core PostgreSQL, helper W3C e persistencia minima.
- `docker run --rm -v "${PWD}:/src" -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet build otel-poc.sln` continuou passando apos integrar a feature no `NotificationWorker`.
- `docker compose up -d --build order-service processing-worker notification-worker kafka postgres otelcol lgtm` passou com o `notification-worker` recriado.
- O primeiro teste integrado expôs `42P01 relation "notification_results" does not exist`; o problema foi corrigido trocando `EnsureCreatedAsync()` por DDL explicito `IF NOT EXISTS` no startup do `NotificationWorker`.
- Um `POST /orders` real gerou consumo e persistencia bem-sucedida no `NotificationWorker`, com linha em `notification_results` contendo `persisted_at_utc` e `trace_id`.
- Uma mensagem JSON malformada publicada manualmente em `notifications` gerou `invalid_payload`, nao criou nova linha no PostgreSQL e produziu trace de erro contendo apenas `kafka consume notifications` no `notification-worker`.
- Com o PostgreSQL parado, uma mensagem valida em `notifications` gerou `persistence_failed`, manteve o container `notification-worker` em `Up` e produziu trace com span `kafka consume notifications` e span DB em erro no `notification-worker`.
- Com o Kafka parado temporariamente, o `notification-worker` permaneceu em `Up` e passou a emitir logs explicitos `Classification=consume_failed` via `SetErrorHandler(...)` do consumer.
- A consulta ao Tempo pelo trace id `a17e81262a971899536d2992b16c7ee1` confirmou o fluxo feliz completo `POST /orders` -> `kafka publish orders` -> `kafka consume orders` -> `GET /orders/{id}` -> `kafka publish notifications` -> `kafka consume notifications` -> span DB no mesmo trace.
- A consulta ao Tempo pelos trace ids `d4ab25654ff660dbe96265871637f854` e `0f99791c5cd50a1a24908ef19660031e` confirmou respectivamente os caminhos `invalid_payload` sem span DB bem-sucedido e `persistence_failed` com erro no hop de banco.
- Um evento manual com `orderId` inexistente gerou `404` no `GET /orders/{id}`, warning estruturado no worker e ausencia de mensagem correspondente em `notifications`; o Tempo confirmou ausencia de span `kafka publish notifications` para o trace `11111111111111111111111111111111`.
- Com o `OrderService` parado, um evento manual gerou falha tecnica por timeout no enriquecimento HTTP, sem derrubar o `processing-worker` nem produzir mensagem em `notifications`.
- Com um responder HTTP 500 efemero na rede `otel-demo`, um worker temporario validou o caminho `5xx`, e o Tempo confirmou apenas `kafka consume orders` + `GET` com erro no trace `77777777777777777777777777777777`, sem span de producer subsequente.
- Um evento manual sem headers W3C foi consumido com warning de contexto ausente, iniciou novo trace no `ProcessingWorker`, completou `GET /orders/{id}` com `200` e publicou normalmente em `notifications`.
- `docker run --rm -v "${PWD}:/src" -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet build otel-poc.sln` continuou passando apos consolidar o helper W3C compartilhado em `src/Shared/W3CTraceContext.cs`.
- `docker compose up -d --build order-service processing-worker notification-worker kafka postgres otelcol lgtm` voltou a passar apos copiar `src/Shared/` nos tres Dockerfiles.
- Um `POST /orders` real gerou `traceparent` em `orders` e `notifications`; a linha persistida em `notification_results` para `5bf42ec2-f1a4-478c-b971-0c2807eea3c4` confirmou `trace_id = 085c91ec7c3a83fc13f7ca7a835960be`, e o Tempo confirmou o fluxo feliz completo nesse mesmo trace.
- Um evento manual valido sem headers W3C em `orders` iniciou novo trace local `0d1ff182b3756e771ae7e5bcb9687141` no `ProcessingWorker`, publicou normalmente em `notifications`, persistiu nova linha correlata e o Tempo mostrou `kafka consume orders` -> `GET /orders/{id}` -> `kafka publish notifications` -> `kafka consume notifications` sem `POST /orders` no mesmo trace.
- Um evento manual valido sem headers W3C em `notifications` iniciou novo trace local `ce75c561d983cae17a4d478b2b8b08b2` no `NotificationWorker`, persistiu normalmente no PostgreSQL e o Tempo mostrou `kafka consume notifications` e o span DB `otelpoc` nesse trace local.

