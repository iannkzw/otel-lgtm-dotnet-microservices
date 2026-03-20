# Docker Compose Infrastructure — Tasks

**Design**: `.specs/features/docker-compose-infra/design.md`
**Status**: Done

---

## Implementation Status (2026-03-19)

- T1-T11: Implementados e validados

---

## Execution Plan

### Phase 1: Foundation — Infra de terceiros (Paralelo OK)

```
T1 (Zookeeper) ──┐
                  ├──→ T3 (Kafka healthcheck depends_on)
T2 (Kafka) ───────┘
T4 (PostgreSQL) ──────────────→ (independente)
```

### Phase 2: Dockerfiles dos serviços .NET (Paralelo OK)

```
T5 (Dockerfile OrderService) ──┐
T6 (Dockerfile ProcessingW.)  ─┼──→ T8 (Compose blocos de serviços)
T7 (Dockerfile NotificationW.)─┘
```

### Phase 3: Integração e validação (Sequencial)

```
T8 → T9 (variáveis de ambiente) → T10 (depends_on + healthchecks) → T11 (smoke test)
```

---

## Task Breakdown

### T1: Adicionar bloco Zookeeper ao docker-compose.yaml

**What**: Inserir serviço `zookeeper` com imagem `confluentinc/cp-zookeeper:7.5.0`, variáveis `ZOOKEEPER_CLIENT_PORT=2181` e `ZOOKEEPER_TICK_TIME=2000`, e rede `otel-demo`
**Where**: `docker-compose.yaml`
**Depends on**: Nenhum
**Reuses**: Rede `otel-demo` existente no arquivo

**Done when**:
- [x] Serviço `zookeeper` presente no `docker-compose.yaml`
- [x] Container sobe sem erro (`docker-compose up -d zookeeper`)
- [x] Porta 2181 responde internamente

---

### T2: Adicionar bloco Kafka Broker ao docker-compose.yaml

**What**: Inserir serviço `kafka` com imagem `confluentinc/cp-kafka:7.5.0`, variáveis de listeners (`PLAINTEXT://kafka:9092`), `KAFKA_ZOOKEEPER_CONNECT=zookeeper:2181`, `KAFKA_AUTO_CREATE_TOPICS_ENABLE=true`, e `depends_on: [zookeeper]`
**Where**: `docker-compose.yaml`
**Depends on**: T1

**Done when**:
- [x] Serviço `kafka` presente no `docker-compose.yaml`
- [x] Container sobe sem erro após Zookeeper estar pronto
- [x] `kafka-broker-api-versions --bootstrap-server localhost:9092` retorna versões sem erro

---

### T3: Adicionar healthcheck ao Kafka e Zookeeper

**What**: Adicionar bloco `healthcheck` em ambos os serviços com comandos apropriados para cada container (`echo ruok | nc localhost 2181` para Zookeeper e `kafka-broker-api-versions --bootstrap-server localhost:9092` para Kafka)
**Where**: `docker-compose.yaml`
**Depends on**: T1, T2

**Done when**:
- [x] `docker-compose ps` mostra status `healthy` para `zookeeper` e `kafka` após startup
- [x] Serviços dependentes do Kafka só sobem depois que Kafka estiver `healthy`

---

### T4: Adicionar bloco PostgreSQL ao docker-compose.yaml

**What**: Inserir serviço `postgres` com imagem `postgres:16-alpine`, variáveis `POSTGRES_DB=otelpoc`, `POSTGRES_USER=poc`, `POSTGRES_PASSWORD=poc`, healthcheck com `pg_isready`, volume nomeado para persistência e rede `otel-demo`
**Where**: `docker-compose.yaml`
**Depends on**: Nenhum (independente de T1/T2)

**Done when**:
- [x] Serviço `postgres` presente no `docker-compose.yaml`
- [x] `docker-compose ps` mostra status `healthy` para `postgres`
- [x] `docker-compose exec postgres psql -U poc -d otelpoc -c "\l"` lista o banco sem erro

---

### T5: Criar Dockerfile multi-stage para OrderService

**What**: Criar `Dockerfile` com stage `build` (sdk:10.0) e stage `runtime` (aspnet:10.0) conforme o padrão do design; expõe porta 8080
**Where**: `src/OrderService/Dockerfile`
**Depends on**: Nenhum

**Done when**:
- [x] `docker compose build order-service` conclui sem erro
- [x] Imagem resultante usa `aspnet:10.0` como base (não SDK)
- [ ] Tamanho da imagem < 300 MB

---

### T6: Criar Dockerfile multi-stage para ProcessingWorker

**What**: Criar `Dockerfile` com stage `build` (sdk:10.0) e `runtime` (aspnet:10.0); sem porta exposta (worker puro)
**Where**: `src/ProcessingWorker/Dockerfile`
**Depends on**: Nenhum

**Done when**:
- [x] `docker compose build processing-worker` conclui sem erro
- [x] Imagem usa `aspnet:10.0` como base

---

### T7: Criar Dockerfile multi-stage para NotificationWorker

**What**: Criar `Dockerfile` com stage `build` (sdk:10.0) e `runtime` (aspnet:10.0); sem porta exposta
**Where**: `src/NotificationWorker/Dockerfile`
**Depends on**: Nenhum

**Done when**:
- [x] `docker compose build notification-worker` conclui sem erro
- [x] Imagem usa `aspnet:10.0` como base

---

### T8: Adicionar blocos dos 3 serviços .NET ao docker-compose.yaml

**What**: Inserir serviços `order-service`, `processing-worker` e `notification-worker` com `build.context: .`, `dockerfile` apontando para cada projeto em `src/`, mapeamento de porta para `order-service` (8080:8080) e `depends_on` para kafka/postgres
**Where**: `docker-compose.yaml`
**Depends on**: T3, T4, T5, T6, T7

**Done when**:
- [x] Três serviços presentes no `docker-compose.yaml`
- [x] `docker compose config` valida sem erro
- [x] `docker compose build` conclui sem erro

---

### T9: Configurar variáveis de ambiente dos serviços .NET no Compose

**What**: Adicionar seção `environment` em cada serviço .NET com `OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_SERVICE_NAME`, `KAFKA_BOOTSTRAP_SERVERS` e, onde aplicável, `POSTGRES_CONNECTION_STRING` (conforme tabela do design)
**Where**: `docker-compose.yaml`
**Depends on**: T8

**Done when**:
- [x] Todas as variáveis definidas no design estão presentes nos serviços corretos
- [x] Nenhum endereço hardcoded no código-fonte (validação por grep)
- [x] `docker compose config` exibe as variáveis resolvidas corretamente

---

### T10: Configurar depends_on com condition: service_healthy

**What**: Atualizar `depends_on` dos serviços .NET para usar `condition: service_healthy` para `kafka` e `postgres`, garantindo ordem de inicialização correta
**Where**: `docker-compose.yaml`
**Depends on**: T3, T4, T9

**Done when**:
- [x] `order-service` só inicia depois que `kafka` e `postgres` estão `healthy`
- [x] `processing-worker` só inicia depois que `kafka` está `healthy`
- [x] `notification-worker` só inicia depois que `kafka` e `postgres` estão `healthy`

---

### T11: Smoke test — validar ambiente completo

**What**: Executar `docker-compose up -d` do zero e verificar que todos os containers atingem estado `Up`/`healthy`
**Where**: Execução local (não cria arquivo)
**Depends on**: T10

**Done when**:
- [x] `docker compose ps` mostra todos os containers como `Up` ou `healthy`
- [x] Logs dos serviços .NET não contêm `Exception` ou `Error` em nível `FATAL`/`CRITICAL` nos primeiros 30 segundos
- [x] `docker compose logs order-service` mostra linha de startup sem erro de conexão

---

## Parallel Execution Map

```
Phase 1:
  T1 ──→ T2 ──→ T3
  T4 (independente)

Phase 2 (após T3 e T4):
  T5 ──┐
  T6 ──┼──→ T8
  T7 ──┘

Phase 3 (após T8):
  T8 ──→ T9 ──→ T10 ──→ T11
```
