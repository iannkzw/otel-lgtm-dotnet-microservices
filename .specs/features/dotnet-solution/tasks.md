# .NET Solution — Tasks

**Design**: `.specs/features/dotnet-solution/design.md`
**Status**: Implemented

---

## Implementation Status (2026-03-19)

- T1: Implementado; `global.json` e `Directory.Build.props` criados
- T2: Implementado; `otel-poc.sln` criado com os 3 projetos
- T3: Implementado e validado via build em SDK 10 container
- T4: Implementado e validado via build em SDK 10 container
- T5: Implementado e validado via build em SDK 10 container
- T6a: Implementado e validado com `docker build`
- T6b: Implementado e validado com `docker build`
- T6c: Implementado e validado com `docker build`
- T7: Validado com `docker run ... mcr.microsoft.com/dotnet/sdk:10.0 dotnet build otel-poc.sln`
- T8: Validado com `docker build` dos 3 projetos; imagens geradas abaixo de 300 MB

**Nota de ambiente:** o host Windows atual não possui SDK 10 instalado, então `dotnet build otel-poc.sln` falha localmente por ausência de SDK compatível. A validação da solution foi executada com sucesso em container Docker com SDK 10.

---

## Execution Plan

### Phase 1: Foundation (Sequencial)

```
T1 (global.json + Directory.Build.props) → T2 (Solution .sln)
```

### Phase 2: Projetos (Paralelo OK após T2)

```
       ┌→ T3 (OrderService csproj + Program.cs) ──┐
T2 ────┼→ T4 (ProcessingWorker csproj + Program.cs)─┼──→ T7 (dotnet build smoke test)
       └→ T5 (NotificationWorker csproj + Program.cs)┘
```

### Phase 3: Dockerfiles (Paralelo OK após T3/T4/T5)

```
       ┌→ T6a (Dockerfile OrderService) ──┐
T3-T5 ─┼→ T6b (Dockerfile ProcessingW.) ──┼──→ T8 (docker build smoke test)
       └→ T6c (Dockerfile NotificationW.)─┘
```

---

## Task Breakdown

### T1: Criar Directory.Build.props

**What**: Criar `global.json` com a versão do SDK .NET 10 e `Directory.Build.props` na raiz do repositório com `TargetFramework`, `Nullable`, `ImplicitUsings` e versões centralizadas dos 4 pacotes OTel definidos no design
**Where**: `Directory.Build.props` (raiz)
**Depends on**: Nenhum

**Done when**:
- [x] `global.json` existe na raiz ao lado de `docker-compose.yaml`
- [x] Arquivo existe na raiz ao lado de `docker-compose.yaml`
- [x] Contém `<TargetFramework>net10.0</TargetFramework>`
- [x] Contém as 4 referências de pacotes OTel centralizadas no `Directory.Build.props`
- [ ] `dotnet --version` resolve para a versão definida no `global.json`
- [x] `dotnet restore` em qualquer projeto da solution não dá erro de versão

---

### T2: Criar solution otel-poc.sln

**What**: Criar `otel-poc.sln` na raiz e adicionar os 3 projetos (que ainda não existem — a solution pode ser criada vazia e os projetos adicionados nas tarefas seguintes)
**Where**: `otel-poc.sln` (raiz)
**Depends on**: T1

**Done when**:
- [x] `otel-poc.sln` existe na raiz
- [x] `dotnet sln list` exibe os 3 projetos após T3/T4/T5

---

### T3: Criar projeto OrderService

**What**: Criar projeto WebAPI com `dotnet new webapi -n OrderService -o src/OrderService`, simplificar o template para seguir o estilo Minimal API e adicionar à solution; criar `appsettings.json` com `Logging:LogLevel:Default=Information`
**Where**: `src/OrderService/`
**Depends on**: T2

**Done when**:
- [x] `src/OrderService/OrderService.csproj` existe com SDK `Microsoft.NET.Sdk.Web`
- [x] `Program.cs` segue o estilo Minimal API (sem controllers)
- [x] `dotnet build src/OrderService/OrderService.csproj` conclui sem erros
- [x] Referências OTel presentes no `.csproj` sem versão hardcoded (versão vem do `Directory.Build.props`)
- [x] Projeto registrado no `otel-poc.sln`

---

### T4: Criar projeto ProcessingWorker

**What**: Criar projeto Worker Service com `dotnet new worker -n ProcessingWorker -o src/ProcessingWorker` e adicionar à solution
**Where**: `src/ProcessingWorker/`
**Depends on**: T2

**Done when**:
- [x] `src/ProcessingWorker/ProcessingWorker.csproj` existe com SDK `Microsoft.NET.Sdk.Worker`
- [x] `dotnet build src/ProcessingWorker/ProcessingWorker.csproj` conclui sem erros
- [x] Referências OTel presentes no `.csproj`
- [x] Projeto registrado no `otel-poc.sln`

---

### T5: Criar projeto NotificationWorker

**What**: Criar projeto Worker Service com `dotnet new worker -n NotificationWorker -o src/NotificationWorker` e adicionar à solution
**Where**: `src/NotificationWorker/`
**Depends on**: T2

**Done when**:
- [x] `src/NotificationWorker/NotificationWorker.csproj` existe com SDK `Microsoft.NET.Sdk.Worker`
- [x] `dotnet build src/NotificationWorker/NotificationWorker.csproj` conclui sem erros
- [x] Referências OTel presentes no `.csproj`
- [x] Projeto registrado no `otel-poc.sln`

---

### T6a: Criar Dockerfile para OrderService

**What**: Criar Dockerfile multi-stage para `OrderService` copiando `global.json` e `Directory.Build.props` no stage de build conforme padrão do design
**Where**: `src/OrderService/Dockerfile`
**Depends on**: T3

**Done when**:
- [x] Arquivo existe em `src/OrderService/Dockerfile`
- [x] Stage `build` usa `mcr.microsoft.com/dotnet/sdk:10.0`
- [x] Stage `runtime` usa `mcr.microsoft.com/dotnet/aspnet:10.0`
- [x] `global.json` e `Directory.Build.props` são copiados antes do `dotnet restore`

---

### T6b: Criar Dockerfile para ProcessingWorker

**What**: Criar Dockerfile multi-stage para `ProcessingWorker` seguindo o mesmo padrão (sem EXPOSE — worker puro)
**Where**: `src/ProcessingWorker/Dockerfile`
**Depends on**: T4

**Done when**:
- [x] Arquivo existe em `src/ProcessingWorker/Dockerfile`
- [x] Stage `runtime` usa `mcr.microsoft.com/dotnet/aspnet:10.0`
- [x] Nenhuma instrução `EXPOSE` (worker sem porta)

---

### T6c: Criar Dockerfile para NotificationWorker

**What**: Criar Dockerfile multi-stage para `NotificationWorker` seguindo o mesmo padrão (sem EXPOSE)
**Where**: `src/NotificationWorker/Dockerfile`
**Depends on**: T5

**Done when**:
- [x] Arquivo existe em `src/NotificationWorker/Dockerfile`
- [x] Stage `runtime` usa `mcr.microsoft.com/dotnet/aspnet:10.0`
- [x] Nenhuma instrução `EXPOSE`

---

### T7: Smoke test — dotnet build da solution

**What**: Executar `dotnet build otel-poc.sln` e validar saída
**Where**: Execução local (não cria arquivo)
**Depends on**: T3, T4, T5

**Done when**:
- [x] `dotnet build otel-poc.sln` retorna `Build succeeded. 0 Error(s)`
- [x] `dotnet sln list` exibe os 3 projetos
- [x] `OrderService` inicia no estilo Minimal API, sem controllers gerados pelo template

---

### T8: Smoke test — docker build dos 3 projetos

**What**: Executar `docker build` para cada projeto a partir do contexto da raiz do repositório e validar que as imagens são geradas com sucesso
**Where**: Execução local (não cria arquivo)
**Depends on**: T6a, T6b, T6c, T7

**Done when**:
- [x] `docker build -t order-service -f src/OrderService/Dockerfile .` conclui sem erro
- [x] `docker build -t processing-worker -f src/ProcessingWorker/Dockerfile .` conclui sem erro
- [x] `docker build -t notification-worker -f src/NotificationWorker/Dockerfile .` conclui sem erro
- [x] Nenhuma imagem tem tamanho > 300 MB

---

## Parallel Execution Map

```
Phase 1 (Sequencial):
  T1 ──→ T2

Phase 2 (Paralelo após T2):
  T2 ──┬──→ T3 ──→ T6a ──┐
       ├──→ T4 ──→ T6b ──┼──→ T8
       └──→ T5 ──→ T6c ──┘

Smoke tests:
  T3+T4+T5 ──→ T7
  T6a+T6b+T6c+T7 ──→ T8
```
