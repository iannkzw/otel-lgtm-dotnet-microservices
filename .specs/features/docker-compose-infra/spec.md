# Docker Compose Infrastructure — Specification

**Milestone**: M1 — Infraestrutura e Esqueleto dos Serviços
**Status**: Blocked by .NET Solution

---

## Problem Statement

O `docker-compose.yaml` atual do `otel-demo-main` não inclui Kafka, Zookeeper, PostgreSQL nem os 3 serviços .NET da PoC. Sem esse ambiente completo, é impossível validar traces distribuídos entre os serviços. Precisamos estender a infra existente para que todos os containers subam com um único `docker-compose up -d`.

## Goals

- [ ] Kafka + Zookeeper rodando e acessíveis internamente na rede Docker
- [ ] PostgreSQL rodando e acessível pelos serviços .NET
- [ ] Os 3 serviços .NET (`OrderService`, `ProcessingWorker`, `NotificationWorker`) buildados e iniciados pelo Compose
- [ ] Todos os containers se comunicam entre si sem erro de rede
- [ ] Variáveis de ambiente centralizam a configuração de OTLP, Kafka e Postgres

## Out of Scope

- Schema Registry ou Avro — será avaliado em iteração futura
- Dead Letter Queue (DLQ) — Future Consideration do roadmap
- Health checks detalhados com retry policy (além do básico do Compose)
- Multi-tenant (`X-Scope-OrgID`) — Future Consideration

---

## User Stories

### P1: Ambiente completo sobe com um único comando ⭐ MVP

**User Story**: Como desenvolvedor, quero executar `docker-compose up -d` e ter todos os containers do ambiente (infra e serviços) iniciando sem erro, para poder desenvolver e validar a PoC localmente.

**Why P1**: Sem isso nenhuma outra feature pode ser validada — é o pré-requisito absoluto de M1.

**Acceptance Criteria**:

1. WHEN `docker-compose up -d` é executado numa máquina limpa (com imagens já baixadas) THEN todos os containers SHALL atingir status `healthy` ou `running` em até 2 minutos
2. WHEN Kafka broker inicia THEN ele SHALL estar acessível na porta interna `9092` da rede Docker
3. WHEN PostgreSQL inicia THEN ele SHALL aceitar conexões com as credenciais definidas nas variáveis de ambiente
4. WHEN os serviços .NET são buildados THEN cada Dockerfile SHALL usar multi-stage build produzindo imagem a partir de `mcr.microsoft.com/dotnet/aspnet`
5. WHEN um serviço .NET falha no startup THEN os logs SHALL mostrar mensagem de erro com stack trace legível

**Independent Test**: Executar `docker-compose up -d` seguido de `docker-compose ps` e verificar que todos os containers estão em `Up`.

---

### P1: Serviços .NET recebem configuração via variáveis de ambiente ⭐ MVP

**User Story**: Como desenvolvedor, quero configurar OTLP endpoint, Kafka brokers e connection string do Postgres via variáveis de ambiente no Compose, para não ter segredos ou endereços hardcoded no código.

**Why P1**: Boa prática de 12-factor e requisito para que o ambiente funcione sem alterar código.

**Acceptance Criteria**:

1. WHEN o Compose define `OTEL_EXPORTER_OTLP_ENDPOINT` THEN os serviços .NET SHALL enviar traces para esse endpoint
2. WHEN o Compose define `KAFKA_BOOTSTRAP_SERVERS` THEN os workers SHALL usar esse endereço para conectar ao Kafka
3. WHEN o Compose define `POSTGRES_CONNECTION_STRING` THEN o `OrderService` e `NotificationWorker` SHALL usar essa string para conectar ao banco
4. WHEN uma variável de ambiente obrigatória estiver ausente THEN o serviço SHALL falhar no startup com mensagem clara indicando qual variável está faltando

**Independent Test**: Alterar o valor de `KAFKA_BOOTSTRAP_SERVERS` para um endereço inválido e verificar que o worker loga erro de conexão com o valor incorreto.

---

### P2: Rede Docker isolada com service discovery por nome

**User Story**: Como desenvolvedor, quero que os serviços se comuniquem usando nomes de serviço (ex: `kafka:9092`, `postgres:5432`, `otelcol:4317`) em vez de IPs, para que a configuração seja estável e legível.

**Why P2**: Sem service discovery por nome a configuração fica frágil (IPs dinâmicos), mas é possível validar M1 com IPs se necessário.

**Acceptance Criteria**:

1. WHEN qualquer serviço tenta conectar a `kafka:9092` THEN a conexão SHALL ser resolvida corretamente
2. WHEN qualquer serviço tenta conectar a `postgres:5432` THEN a conexão SHALL ser resolvida corretamente
3. WHEN qualquer serviço tenta conectar a `otelcol:4317` THEN a conexão SHALL ser resolvida corretamente

**Independent Test**: Executar `docker-compose exec order-service ping kafka` e verificar resolução DNS.

---

## Edge Cases

- WHEN o host não tem porta 9092 disponível THEN o compose SHALL mapear para uma porta alternativa sem conflito
- WHEN o PostgreSQL ainda não está pronto e um serviço .NET tenta conectar THEN o serviço SHALL aguardar e retentar (via `depends_on` com `condition: service_healthy`)
- WHEN o Kafka broker ainda não elegeu líder e um producer tenta publicar THEN o producer SHALL aguardar e logar o retry

---

## Success Criteria

- [ ] `docker-compose up -d` funciona em ambiente limpo sem erros manuais de configuração
- [ ] `docker-compose ps` mostra todos os containers como `Up` ou `healthy`
- [ ] Logs de cada serviço .NET mostram conexão bem-sucedida com Kafka e Postgres no startup

---

## Implementation Notes

- 2026-03-19: Infra base de terceiros adicionada ao compose (`zookeeper`, `kafka`, `postgres`) com rede explícita `otel-demo`, volume nomeado para Postgres e healthchecks para Kafka e Postgres.
- 2026-03-19: Smoke test executado com sucesso para `zookeeper`, `kafka` e `postgres`; `docker compose ps` mostrou os 3 containers como `healthy` e `psql` confirmou acesso ao banco `otelpoc`.
- 2026-03-19: A inclusão dos 3 serviços .NET permaneceu bloqueada pela ausência da solution `otel-poc.sln`, dos projetos em `src/` e dos Dockerfiles que são entregues pela feature `.NET Solution`.
