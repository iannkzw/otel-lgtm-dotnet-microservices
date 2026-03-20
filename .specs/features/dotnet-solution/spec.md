# .NET Solution — Specification

**Milestone**: M1 — Infraestrutura e Esqueleto dos Serviços
**Status**: Planned

---

## Problem Statement

Não existe ainda nenhuma estrutura de código .NET para a PoC. Precisamos criar a solution com os 3 projetos (`OrderService`, `ProcessingWorker`, `NotificationWorker`) e garantir que todos compilam, têm configuração de build consistente e possuem Dockerfiles capazes de gerar imagens de produção.

## Goals

- [ ] Solution `otel-poc.sln` compilável com `dotnet build` sem erros
- [ ] 3 projetos criados com templates adequados (Minimal API para OrderService, Worker Service para os workers)
- [ ] `Directory.Build.props` centraliza TargetFramework e referências comuns de pacotes OTel, enquanto `global.json` fixa a versão do SDK
- [ ] Cada projeto tem um `Dockerfile` multi-stage funcional

## Out of Scope

- Lógica de negócio (endpoints reais, consumers Kafka, queries SQL) — isso é M2
- Testes automatizados — serão adicionados no projeto conforme necessidade futura
- CI/CD pipeline — não está no escopo desta PoC

---

## User Stories

### P1: Solution com 3 projetos compiláveis ⭐ MVP

**User Story**: Como desenvolvedor, quero executar `dotnet build otel-poc.sln` e ter os 3 projetos compilando sem erros, para validar que o esqueleto está correto antes de adicionar lógica.

**Why P1**: Base de tudo — sem isso nenhuma outra feature .NET pode ser implementada.

**Acceptance Criteria**:

1. WHEN `dotnet build otel-poc.sln` é executado THEN o build SHALL concluir com `Build succeeded` e 0 erros
2. WHEN `OrderService` é criado THEN ele SHALL seguir o estilo Minimal API com .NET 10
3. WHEN `ProcessingWorker` é criado THEN ele SHALL ser um projeto `worker` com .NET 10
4. WHEN `NotificationWorker` é criado THEN ele SHALL ser um projeto `worker` com .NET 10
5. WHEN qualquer projeto é buildado isoladamente THEN o build SHALL concluir sem erros

**Independent Test**: `dotnet build otel-poc.sln` em ambiente com .NET 10 SDK instalado ou com a versão definida em `global.json`.

---

### P1: Configuração centralizada via Directory.Build.props + global.json ⭐ MVP

**User Story**: Como desenvolvedor, quero que o SDK fique pinado em `global.json` e que TargetFramework, pacotes OTel e configurações de build fiquem centralizados no `Directory.Build.props`, para que os 3 projetos compartilhem a mesma base sem duplicação.

**Why P1**: Evita drift de versões entre projetos e reduz manutenção futura.

**Acceptance Criteria**:

1. WHEN `global.json` existe na raiz THEN o SDK SHALL ser resolvido para a versão definida nele
2. WHEN `Directory.Build.props` existe na raiz da solution THEN todos os projetos SHALL herdar suas propriedades automaticamente sem referência explícita
3. WHEN a versão de um pacote OTel é atualizada no `Directory.Build.props` THEN todos os projetos SHALL usar a nova versão após `dotnet restore`
4. WHEN um novo projeto é criado na solution THEN ele SHALL herdar as propriedades base sem configuração adicional

**Independent Test**: Verificar que `dotnet --version` respeita o `global.json` e que `dotnet list package` em cada projeto mostra as mesmas versões dos pacotes OTel.

---

### P1: Dockerfiles multi-stage para os 3 serviços ⭐ MVP

**User Story**: Como desenvolvedor, quero um `Dockerfile` para cada serviço que faça build e gere imagem de produção, para que o `docker-compose` possa construir as imagens localmente.

**Why P1**: Requisito direto da feature `docker-compose-infra` — o Compose depende desses Dockerfiles.

**Acceptance Criteria**:

1. WHEN `docker build` é executado em qualquer projeto THEN a imagem SHALL ser gerada com sucesso usando build multi-stage
2. WHEN a imagem é gerada THEN ela SHALL usar `mcr.microsoft.com/dotnet/aspnet:10.0` como base (não SDK)
3. WHEN o container é iniciado a partir da imagem THEN o processo SHALL iniciar sem erro
4. WHEN a imagem é inspecionada THEN ela NÃO SHALL conter o SDK do .NET (apenas o runtime)

**Independent Test**: `docker build -t order-service ./src/OrderService` e verificar tamanho da imagem < 300 MB.

---

### P2: Estrutura de pastas seguindo convenções .NET

**User Story**: Como desenvolvedor, quero que os projetos sigam a estrutura convencional do .NET (`src/`, `Program.cs`, `appsettings.json`), para que qualquer desenvolvedor .NET reconheça a estrutura imediatamente.

**Why P2**: Boa prática que facilita onboarding, mas o projeto funciona sem isso.

**Acceptance Criteria**:

1. WHEN o repositório é clonado THEN a estrutura SHALL ter `src/{ProjectName}/` para cada projeto
2. WHEN cada projeto é inspecionado THEN ele SHALL ter `Program.cs`, `appsettings.json` e `appsettings.Development.json`

**Independent Test**: Inspecionar o diretório `src/` e verificar estrutura com `tree`.

---

## Edge Cases

- WHEN .NET SDK não está instalado localmente THEN o build no Docker (multi-stage) SHALL funcionar sem dependência local
- WHEN `Directory.Build.props` tem erro de sintaxe THEN `dotnet build` SHALL falhar com mensagem clara indicando o arquivo problemático
- WHEN um pacote OTel não tem versão compatível com .NET 10 THEN o restore SHALL falhar com erro de compatibilidade legível

---

## Success Criteria

- [ ] `dotnet build otel-poc.sln` retorna `Build succeeded. 0 Error(s)`
- [ ] `docker build` funciona para os 3 projetos sem modificações manuais
- [ ] `Directory.Build.props` contém pelo menos os pacotes OTel core sem duplicação nos `.csproj`
- [ ] `global.json` fixa explicitamente a versão do SDK usada pela solution
