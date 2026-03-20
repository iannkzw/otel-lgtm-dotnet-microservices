# NotificationWorker Consumer + Persistência — Specification

**Milestone**: M2 — Fluxo de Eventos End-to-End
**Status**: Specified

---

## Problem Statement

O topic `notifications` já recebe um payload mínimo enriquecido pelo
`ProcessingWorker`, com `traceparent` e `tracestate` propagados a partir do
trace iniciado em `POST /orders`. Porém, o `NotificationWorker` ainda executa
apenas heartbeats e não fecha o último hop distribuído do milestone.

Esta feature precisa fazer o `NotificationWorker` consumir o topic
`notifications`, reconstruir o contexto W3C a partir dos headers Kafka e
persistir um resultado mínimo e observável no PostgreSQL sem alterar os
contratos já estabilizados de `OrderResponse` e do payload publicado em
`notifications`. O resultado esperado é um trace único no Tempo conectando
`OrderService` -> `ProcessingWorker` -> `NotificationWorker`, com erro e sucesso
distinguíveis por spans, logs e persistência.

## Goals

- [ ] Consumir mensagens do topic Kafka `notifications` no `NotificationWorker`
- [ ] Extrair `traceparent` e `tracestate` dos headers Kafka para continuar o
      mesmo trace iniciado em `POST /orders`
- [ ] Criar span manual de consumo/processamento com `ActivityKind.Consumer`
- [ ] Persistir no PostgreSQL um resultado mínimo e observável sem alterar o
      contrato do payload consumido
- [ ] Registrar spans e logs suficientes para fechar o caminho distribuído de
      M2 no Tempo
- [ ] Distinguir falhas de consumo, payload inválido e persistência sem derrubar
      o host
- [ ] Definir critérios objetivos de validação local e no Tempo para o caminho
      feliz e para os principais erros

## Out of Scope

- Alterar os contratos já consolidados de `OrderResponse`
- Alterar o payload mínimo já publicado em `notifications`
- Retry, backoff dedicado, DLQ, transactional outbox ou garantias avançadas de
  entrega
- Integração com provedor real de e-mail, SMS ou notificação externa
- Métricas customizadas, dashboards e alertas de M3
- Refatorar os helpers Kafka para biblioteca compartilhada se isso não for
  necessário para fechar M2

---

## Message Contracts

### Consumed message: topic `notifications`

Origem: `ProcessingWorker.Contracts.NotificationRequestedEvent`

**Expected payload**:

```json
{
  "orderId": "58ab9539-9e92-4002-982b-e6d16fe178ca",
  "description": "demo order",
  "status": "published",
  "createdAtUtc": "2026-03-19T18:30:00.0000000+00:00",
  "publishedAtUtc": "2026-03-19T18:30:00.1500000+00:00",
  "processedAtUtc": "2026-03-19T18:30:01.0000000+00:00"
}
```

**Required headers**:

- `traceparent`: obrigatório para continuar o trace distribuído quando válido
- `tracestate`: opcional, mas deve ser preservado quando presente

O worker não deve mudar esse payload. Toda informação adicional necessária para
persistência e observabilidade deve ficar restrita ao modelo interno e ao banco.

---

## Persistence Contract

### Persisted result: PostgreSQL

O `NotificationWorker` deve persistir um resultado mínimo por mensagem válida
processada, em tabela própria do serviço no PostgreSQL compartilhado da PoC.

**Minimum persisted shape**:

```json
{
  "orderId": "58ab9539-9e92-4002-982b-e6d16fe178ca",
  "description": "demo order",
  "status": "published",
  "createdAtUtc": "2026-03-19T18:30:00.0000000+00:00",
  "publishedAtUtc": "2026-03-19T18:30:00.1500000+00:00",
  "processedAtUtc": "2026-03-19T18:30:01.0000000+00:00",
  "persistedAtUtc": "2026-03-19T18:30:02.0000000+00:00",
  "traceId": "a012646a6f26895d50120b67b84e22d8"
}
```

**Rules**:

- O registro persistido deve copiar os seis campos já consolidados do evento de
  `notifications` sem renomeá-los no contrato externo
- `persistedAtUtc` é interno ao `NotificationWorker` e serve para tornar o hop
  final observável no banco
- `traceId` deve refletir o trace corrente quando disponível, permitindo
  correlacionar a linha persistida com o trace no Tempo
- O schema persistido pode crescer no futuro, mas esta feature deve manter o
  mínimo necessário para demonstrar o fechamento do fluxo distribuído de M2

---

## Failure taxonomy

Esta feature deve distinguir três classes de falha sem derrubar o processo host:

1. **Falha de consumo**: erro técnico ao consumir do Kafka, como exceção de
   `Consume`, perda de conexão com broker ou cancelamento fora do shutdown
   esperado
2. **Payload inválido**: JSON inválido, campos obrigatórios ausentes, `orderId`
   inválido, timestamps impossíveis, ou inconsistência semântica do payload
   recebido
3. **Falha de persistência**: erro de conexão, timeout, constraint ou exceção do
   PostgreSQL/EF Core ao salvar o resultado mínimo

**Expected behavior by class**:

- Falha de consumo: registrar erro estruturado com metadados Kafka, marcar o hop
  atual como falho quando houver span ativo e continuar saudável para a próxima
  iteração
- Payload inválido: registrar warning ou error estruturado com classificação
  `invalid_payload`, não persistir linha no PostgreSQL e continuar saudável
- Falha de persistência: registrar exceção estruturada com `orderId` e
  `TraceId`, marcar o span de banco e o span de consumo como erro, não encerrar
  o host e seguir apto a processar novas mensagens

Retry, DLQ e outbox permanecem fora de escopo. O comportamento desta feature é
tornar a causa observável, não implementar recuperação automática.

---

## User Stories

### P1: Consumir `notifications` e continuar o trace distribuído ⭐ MVP

**User Story**: Como `NotificationWorker`, quero consumir mensagens do topic
`notifications` e continuar o trace a partir dos headers W3C, para fechar o
último hop do fluxo distribuído iniciado em `POST /orders`.

**Why P1**: Sem o consumer real do `NotificationWorker`, M2 ainda termina no
publish do `ProcessingWorker`.

**Acceptance Criteria**:

1. WHEN o `NotificationWorker` receber uma mensagem válida do topic
   `notifications` THEN ele SHALL desserializar `orderId`, `description`,
   `status`, `createdAtUtc`, `publishedAtUtc` e `processedAtUtc`
2. WHEN o header `traceparent` vier válido THEN o worker SHALL extrair o
   contexto pai e iniciar um span manual de consumo com `ActivityKind.Consumer`
3. WHEN o header `tracestate` existir THEN o worker SHALL preservá-lo no
   contexto atual para o restante do processamento
4. WHEN os headers W3C estiverem ausentes ou inválidos THEN o worker SHALL
   iniciar um novo trace, registrar warning estruturado e continuar o
   processamento
5. WHEN o consumo começar THEN o span SHALL registrar tags suficientes para
   identificar topic, operação, chave e grupo de consumo

**Independent Test**: Produzir ou aguardar uma mensagem real em `notifications`,
verificar no log do `notification-worker` o `orderId` consumido e confirmar no
Tempo que existe um span `kafka consume notifications` ligado ao mesmo trace do
`POST /orders` quando o header `traceparent` estiver presente.

---

### P1: Persistir um resultado mínimo e observável no PostgreSQL ⭐ MVP

**User Story**: Como engenheiro de observabilidade, quero que o
`NotificationWorker` persista um resultado mínimo no PostgreSQL, para comprovar
o último hop do fluxo com visibilidade em banco, logs e traces.

**Why P1**: O milestone pede um encerramento material do pipeline, não apenas um
consumer que lê e descarta a mensagem.

**Acceptance Criteria**:

1. WHEN o payload consumido for válido THEN o worker SHALL persistir uma linha
   contendo pelo menos `orderId`, `description`, `status`, `createdAtUtc`,
   `publishedAtUtc`, `processedAtUtc`, `persistedAtUtc` e `traceId`
2. WHEN a persistência ocorrer THEN o trace SHALL conter span de cliente DB com
   `db.system = postgresql` como filho do span de consumo/processamento
3. WHEN a persistência concluir com sucesso THEN o worker SHALL registrar log de
   sucesso com `orderId`, `TraceId`, `SpanId` e timestamp da gravação
4. WHEN a linha for persistida THEN o contrato externo de `OrderResponse` e o
   payload de `notifications` SHALL permanecer inalterados
5. WHEN o fluxo feliz terminar THEN o PostgreSQL SHALL conter um registro que
   permita correlacionar o `orderId` e o `TraceId` com o trace visualizado no
   Tempo

**Independent Test**: Criar um pedido via `POST /orders`, aguardar o consumo em
`NotificationWorker`, consultar a tabela persistida no PostgreSQL e confirmar a
presença dos campos mínimos e do mesmo `TraceId` observado no Tempo.

---

### P1: Tornar payload inválido observável sem persistir nem derrubar o host ⭐ MVP

**User Story**: Como operador, quero distinguir quando a mensagem consumida é
inválida, para saber que a falha ocorreu antes da persistência e sem confundir o
erro com indisponibilidade do banco ou do Kafka.

**Why P1**: A PoC precisa mostrar causalidade clara do erro, não apenas falha
genérica no worker.

**Acceptance Criteria**:

1. WHEN o JSON da mensagem estiver inválido THEN o worker SHALL classificar o
   caso como `invalid_payload`, registrar erro estruturado e não persistir no
   PostgreSQL
2. WHEN `orderId` estiver ausente ou inválido THEN o worker SHALL não tentar
   persistir e SHALL continuar saudável para consumir a próxima mensagem
3. WHEN `status = published` vier sem `publishedAtUtc` THEN o worker SHALL
   tratar a mensagem como inconsistente e classificá-la como `invalid_payload`
4. WHEN qualquer payload inválido for detectado THEN o span de consumo SHALL ser
   marcado com erro observável e SHALL não existir span de persistência bem-
   sucedida para esse processamento
5. WHEN a mensagem inválida terminar de ser tratada THEN o host SHALL permanecer
   em execução sem retry, DLQ ou encerramento do processo

**Independent Test**: Publicar manualmente uma mensagem inválida em
`notifications`, verificar ausência de nova linha no PostgreSQL, presença do log
estruturado classificado como `invalid_payload` e manutenção do worker em
execução.

---

### P1: Tornar falhas de persistência observáveis sem derrubar o host ⭐ MVP

**User Story**: Como operador, quero distinguir quando a mensagem é válida mas a
gravação falha, para comprovar que o pipeline chegou ao último hop e parou no
banco.

**Why P1**: O objetivo da feature inclui diferenciar falha de consumo, payload e
persistência no caminho distribuído completo de M2.

**Acceptance Criteria**:

1. WHEN o PostgreSQL estiver indisponível ou lançar exceção durante a gravação
   THEN o worker SHALL classificar o caso como `persistence_failed`
2. WHEN a falha de persistência ocorrer THEN o span de banco SHALL registrar a
   exceção e o span de consumo/processamento SHALL ser marcado com erro
3. WHEN a persistência falhar THEN o worker SHALL registrar erro estruturado com
   `orderId`, `TraceId`, `SpanId` e o tipo da exceção
4. WHEN a mensagem válida falhar ao persistir THEN o host SHALL continuar
   saudável para consumir mensagens futuras
5. WHEN o erro terminar THEN retry, DLQ e reprocessamento automático SHALL
   continuar fora de escopo

**Independent Test**: Com o PostgreSQL indisponível ou apontando para uma
configuração inválida, consumir uma mensagem válida e verificar no Tempo e nos
logs que o fluxo chegou ao span de banco com erro, sem derrubar o host.

---

### P1: Tornar falhas de consumo observáveis sem parar o worker ⭐ MVP

**User Story**: Como operador, quero identificar falhas técnicas no loop de
consumo Kafka, para diferenciar indisponibilidade do broker de erro de payload
ou de persistência.

**Why P1**: Sem essa separação, todos os erros do worker ficam parecidos nos
logs e o diagnóstico do último hop perde precisão.

**Acceptance Criteria**:

1. WHEN a operação de `Consume` lançar exceção de infraestrutura THEN o worker
   SHALL classificar o caso como `consume_failed`
2. WHEN a falha de consumo acontecer antes de existir payload utilizável THEN o
   worker SHALL registrar metadados Kafka disponíveis e SHALL não tentar
   persistir no banco
3. WHEN a falha de consumo ocorrer THEN o processo host SHALL continuar em
   execução para retomar novas iterações do loop
4. WHEN o broker voltar a responder THEN o worker SHALL voltar a consumir sem
   exigir reinício manual do container
5. WHEN esse cenário ocorrer THEN a implementação SHALL manter fora de escopo
   qualquer política dedicada de retry ou DLQ

**Independent Test**: Induzir indisponibilidade transitória do Kafka ou exceção
controlada no consumo, verificar logs classificados como `consume_failed` e
confirmar que o container do `notification-worker` permanece em estado `Up`.

---

### P2: Fechar o trace distribuído completo de M2 no Tempo

**User Story**: Como pessoa desenvolvedora da PoC, quero ver um trace único do
`POST /orders` até a persistência final do `NotificationWorker`, para comprovar
o valor do tracing distribuído ponta a ponta.

**Why P2**: Esse é o objetivo principal do milestone M2.

**Acceptance Criteria**:

1. WHEN o fluxo feliz completo ocorrer THEN o Tempo SHALL mostrar um único trace
   contendo spans dos três serviços
2. WHEN o `NotificationWorker` consumir e persistir com sucesso THEN o trace
   SHALL incluir pelo menos `kafka consume notifications` e um span DB do
   PostgreSQL no `notification-worker`
3. WHEN o fluxo vier com headers W3C válidos desde o `OrderService` THEN os
   spans dos três serviços SHALL compartilhar o mesmo `TraceId`
4. WHEN o `NotificationWorker` iniciar novo trace por ausência de headers THEN o
   log SHALL deixar claro que houve quebra de correlação sem falha funcional do
   processamento
5. WHEN ocorrer erro de payload ou persistência THEN o trace SHALL indicar em
   qual hop a cadeia terminou

**Independent Test**: Executar o fluxo feliz end-to-end, abrir o trace no Tempo
e confirmar a sequência `POST /orders` -> `kafka publish orders` ->
`kafka consume orders` -> `GET /orders/{id}` ->
`kafka publish notifications` -> `kafka consume notifications` -> span DB.

---

## Edge Cases

- WHEN a mensagem Kafka vier com JSON malformado THEN o worker SHALL registrar o
  erro de desserialização e seguir saudável
- WHEN `processedAtUtc` vier anterior a `createdAtUtc` THEN o worker SHALL
  tratar o payload como inconsistente e não persisti-lo
- WHEN `traceparent` existir mas não puder ser parseado THEN o worker SHALL
  iniciar um novo trace e registrar warning de contexto inválido
- WHEN o PostgreSQL estiver indisponível no startup THEN o host pode iniciar, mas
  a tentativa de persistência SHALL falhar de forma observável em runtime
- WHEN o Kafka estiver temporariamente indisponível THEN o loop SHALL registrar
  `consume_failed` e continuar apto a retomar quando a infraestrutura voltar

## Local Validation Criteria

### Happy path

1. Subir o ambiente com `docker compose up -d --build`
2. Criar um pedido real com `POST /orders`
3. Confirmar que o `ProcessingWorker` publicou a mensagem em `notifications`
4. Confirmar nos logs do `notification-worker` o consumo e a persistência com o
   mesmo `orderId`
5. Consultar diretamente a tabela do `NotificationWorker` no PostgreSQL e
   validar os campos mínimos persistidos

### Error path: invalid payload

1. Publicar manualmente uma mensagem inválida no topic `notifications`
2. Confirmar log classificado como `invalid_payload`
3. Confirmar ausência de nova linha persistida para o `orderId` inválido
4. Confirmar que o `notification-worker` continua em execução

### Error path: persistence failure

1. Tornar o PostgreSQL indisponível ou induzir falha controlada de conexão
2. Produzir uma mensagem válida em `notifications`
3. Confirmar log classificado como `persistence_failed`
4. Confirmar que o worker continua saudável após a exceção

### Error path: consume failure

1. Induzir falha transitória de consumo Kafka
2. Confirmar log classificado como `consume_failed`
3. Confirmar que o loop retoma consumo quando o broker volta

## Tempo Validation Criteria

### Happy path

1. Criar um pedido real com `POST /orders`
2. Confirmar no Tempo um único trace contendo, no mínimo:
   - span root HTTP `POST /orders` no `order-service`
   - span `kafka publish orders` no `order-service`
   - span `kafka consume orders` no `processing-worker`
   - span HTTP `GET /orders/{id}` no `processing-worker`
   - span `kafka publish notifications` no `processing-worker`
   - span `kafka consume notifications` no `notification-worker`
   - span DB de persistência no `notification-worker`
3. Confirmar que todos os spans compartilham o mesmo `TraceId`

### Error path: invalid payload

1. Publicar mensagem inválida em `notifications`
2. Confirmar no Tempo a presença do span `kafka consume notifications` com erro
3. Confirmar ausência de span DB bem-sucedido para esse processamento

### Error path: persistence failure

1. Produzir mensagem válida com o PostgreSQL indisponível
2. Confirmar no Tempo o span de consumo e o span DB com erro no
   `notification-worker`
3. Confirmar que a cadeia termina no hop de banco

## Success Criteria

- [ ] O `NotificationWorker` consome `notifications` e continua o trace usando
      `traceparent` e `tracestate`
- [ ] O PostgreSQL recebe um resultado mínimo e observável com os campos do
      payload e metadados internos de persistência
- [ ] O trace feliz de M2 mostra spans encadeados de `OrderService` ->
      `ProcessingWorker` -> `NotificationWorker` no mesmo `TraceId`
- [ ] Falhas de consumo, payload inválido e persistência ficam distinguíveis por
      logs e spans sem derrubar o host
- [ ] Os contratos já consolidados de `OrderResponse` e do payload de
      `notifications` permanecem intactos