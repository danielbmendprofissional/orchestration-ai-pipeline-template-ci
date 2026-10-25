# template-ci

Template de CI/CD do GitLab para os projetos do **orquestracao-ai**: **.NET 10** e **Angular**, tudo em **container**, publicado em **Kubernetes** (k3s/k3d) via **GitLab Agent**, com imagens no **Container Registry do GitLab**.

Este README é o estado atual do template (não histórico). Ao mudar algo, atualize aqui.

```
ci-template (pai)                         ci-execucao (filho)
  controlar-pipeline  ─┐                    build ─ test ─ image ─ security ─ gate ─ deploy ─ verify
  definir-kind        ─┼─► disparar ──►       dotnet   dotnet  build/   trivy-fs   agrega   migrations   smoke-test
                       │                      angular  angular retag/   opengrep   + bloqueia  + kustomize  ZAP (qa/hml)
                                                               resolve  checkov    por         + rollout    dast-gate
                                                                        betterleaks severidade
                                                                        trivy-image
```

## Princípios

- **Fluxo por tag** (`dev → qa → hml → main`): o `controlar-pipeline` valida tag, promoção, linhagem e bloqueia contrabando de commit. Regras completas em [docs/fluxo-de-tags.md](docs/fluxo-de-tags.md).
- **Build once / deploy many**: só a tag candidata (e hotfix/minor) compila e gera imagem. Promoções usam **a mesma imagem, pelo digest**. A tag final recebe a tag nova no mesmo digest da candidata — nada é recompilado entre qa, hml e produção.
- **Configuração por ambiente fora da imagem**: variáveis do GitLab com escopo de ambiente (deploy) e ConfigMap/Secret no cluster (app).
- **Segurança decide num lugar só**: as ferramentas só geram relatório; o `security-gate` normaliza e bloqueia.

## Como consumir

`.gitlab-ci.yml` do projeto — exemplos completos em [examples/dotnet](examples/dotnet) e [examples/angular](examples/angular):

```yaml
include:
  - project: orchestration-ai/ai-pipeline/template-ci
    file: /ci-template.yml
    ref: v2.0.0          # sempre uma tag, nunca main

variables:
  APP_TECHNOLOGY: dotnet # angular, ou "dotnet angular" (monorepo)
  # ... variáveis da tecnologia (abaixo)
```

O projeto não define `workflow:` — o template já roda só em tag e em push de `qa`/`hml`/`main`.

### Padrão: projeto em workspace (um repositório por tecnologia)

Padrão do orchestration-ai (mesmo desenho do próprio `orquestracao-ai`): um **grupo** por projeto no GitLab e uma **pasta comum** local com um repositório git por componente.

```
GitLab: grupo valuto/                   Local: valuto/        ← clone do valuto-workspace
  ├── valuto-workspace  regras + issues          ├── CLAUDE.md, .claude/, docs/, CONTEXT-MAP.md
  ├── valuto-api        APP_TECHNOLOGY: dotnet   ├── valuto-api/       (.git próprio)
  └── valuto-frontend   APP_TECHNOLOGY: angular  └── valuto-frontend/  (.git próprio)
```

- Cada repositório de código consome este template com **uma** tecnologia — tag, versão, imagem e promoção independentes.
- O `<projeto>-workspace` não tem pipeline de deploy: guarda as regras do ai-base, specs/ADRs e as **issues** do projeto (labels `backend`/`frontend`; MR de `-api`/`-frontend` fecha com `Closes <grupo>/<projeto>-workspace#N`).
- Feature que toca API e front: a API sobe antes e precisa ser retrocompatível (expand/contract) — as duas versões convivem nos ambientes.
- O modo monorepo abaixo continua existindo pra quem quer uma versão única, mas não é o padrão.

### Variáveis — .NET

| Variável | Obrigatória | Descrição |
|---|---|---|
| `DOTNET_SOLUTION` | sim | `.sln`/`.slnx` usada no restore/build |
| `DOTNET_APPS` | sim | `nome=caminho/Projeto.csproj`, separados por espaço. O nome vira imagem (`$CI_REGISTRY_IMAGE/<nome>`) e nome do Deployment |
| `DOTNET_TESTS` | não | `.csproj` de teste, separados por espaço. Vazio = sem job de teste. JUnit no GitLab se o projeto referenciar `JunitXml.TestLogger` |
| `EF_PROJECT` / `EF_STARTUP_PROJECT` | não | Liga as migrations: gera `efbundle` (imagem `migrator`) e `migrations.sql` (artifact, pra revisão) |
| `EF_CONTEXT` | não | Nome do DbContext, se houver mais de um |
| `DOTNET_SDK_IMAGE` / `DOTNET_RUNTIME_IMAGE` | default | `mcr.microsoft.com/dotnet/sdk:10.0` / `aspnet:10.0` |

### Variáveis — Angular

| Variável | Obrigatória | Descrição |
|---|---|---|
| `ANGULAR_DIST_PATH` | sim | Saída do build, relativa ao workdir (ex: `dist/meu-app/browser`) |
| `ANGULAR_WORKDIR` | não | Subpasta do projeto Angular no repositório |
| `ANGULAR_APP_NAME` | default `web` | Nome da imagem e do Deployment |
| `BUILD_COMMAND` / `TEST_COMMAND` | `build` / vazio | Scripts do `package.json`. `TEST_COMMAND` vazio = sem job de teste |
| `NODE_IMAGE` / `NGINX_IMAGE` | default | `node:24-alpine` / `nginxinc/nginx-unprivileged:1.31-alpine` |

O projeto precisa de `nginx/app.conf.template` e `nginx/security-headers.inc.template` — copie de [examples/angular/nginx](examples/angular/nginx). Hardening de path traversal, headers de segurança num arquivo só, CSP por variável de ambiente, `/healthz` e `/config.json` (configuração em runtime, a mesma imagem serve todo ambiente). Com `inlineCritical` ligado o Angular injeta um `onload` inline que o CSP bloqueia — desligue em `angular.json` (comentário no arquivo).

### Variáveis por ambiente (escopo de ambiente no GitLab)

GitLab → Settings → CI/CD → Variables, com **Environment scope** `dev`, `qa`, `hml`, `production`:

| Variável | Descrição |
|---|---|
| `KUBE_CONTEXT` | Contexto do agent, ex: `meu-grupo/meu-projeto:qa`. **Vazio = ambiente sem cluster, deploy pulado** (ex: dev) |
| `K8S_NAMESPACE` | Namespace da app |
| `APP_URL` | URL pública do ambiente — environment do GitLab, smoke-test e ZAP. Vazio = smoke/ZAP pulados |
| `SMOKE_EXTRA_URLS` | Opcional: URLs completas a mais pro smoke-test, separadas por espaço (monorepo: a API no host próprio) |

### Outras variáveis

| Variável | Default | Descrição |
|---|---|---|
| `RUNNER_TAG` | `self-hosted` | Tag do runner |
| `K8S_OVERLAY_DIR` | `deploy/k8s/overlays` | Aplica `<dir>/<PIPELINE_ENV>` (`dev`, `qa`, `hml`, `production`) |
| `MIGRATION_SECRET` / `MIGRATION_SECRET_KEY` | `<projeto>-db` / `connection` | Secret do cluster com a connection string das migrations |
| `HEALTH_PATH` | `/healthz` | Rota do smoke-test (`/healthz` = nginx do Angular; API .NET usa `/health`, convenção do ai-base) |
| `ROLLOUT_TIMEOUT` / `MIGRATION_TIMEOUT` | `5m` / `600` | Limites do deploy |
| `SECURITY_FAIL_ON` | `CRITICAL,HIGH` | Severidades que reprovam o gate |
| `OPENGREP_RULESETS` | `csharp typescript javascript html dockerfile generic` | Pacotes de regra do Opengrep |
| `SKIP_SECURITY` / `SKIP_DEPLOY` | `false` | Pular etapas (pipeline manual) |

### Monorepo (.NET + Angular no mesmo repositório) — alternativa ao workspace

`APP_TECHNOLOGY: "dotnet angular"` e as variáveis das duas tecnologias ([examples/monorepo](examples/monorepo)). Uma tag/versão pro repositório inteiro: build e teste de cada tecnologia em jobs próprios, todas as imagens (`DOTNET_APPS` + `migrator` + `ANGULAR_APP_NAME`) geradas e promovidas juntas, um overlay do Kustomize publica tudo. Nomes de app não podem se repetir (validado no `definir-kind`). `ANGULAR_WORKDIR` aponta a pasta do front — o `nginx/` fica dentro dela. Versionamento independente por componente não existe: o fluxo de tags é por repositório.

## Imagem

Um job, três modos (decididos pelo `controlar-pipeline`):

| Processo | Modo | O que acontece |
|---|---|---|
| tag candidata, hotfix, minor | `build` | `docker build` + push `:$PIPELINE_TAG` |
| tag final vinda de candidata | `retag` | `:vX.Y.Z-rcN` ganha também `:vX.Y.Z` (mesmo digest) |
| promotion / rollback | `resolve` | confirma que `:$PIPELINE_TAG` existe |

Sempre exporta `IMAGE_REFS` (`nome=repo@sha256:...`) — deploy e scan de imagem trabalham por digest. Dockerfile: se o projeto tiver `docker/<nome>.Dockerfile`, usa; senão gera um padrão (aspnet / runtime-deps pro migrator / nginx-unprivileged), não-root, porta 8080.

## Deploy (Kubernetes)

1. **Migrations** (se `EF_PROJECT`): Job no cluster rodando o `efbundle` com a connection string do Secret. Falhou → app não é atualizada.
2. **Kustomize**: `kustomize edit set image <app>=<repo>@<digest>` no overlay do ambiente, `kubectl apply -k`.
3. **Rollout**: `rollout status` de cada Deployment; falhou → `rollout undo` e pipeline vermelha.

Nos manifests, a imagem é só o nome da app (`image: api`) e o Deployment tem o mesmo nome. Produção é `when: manual`. **Banco é só pra frente**: rollback volta a imagem, não o schema (migrations expand/contract).

## Segurança

| Ferramenta | Job | Quando | Cobre |
|---|---|---|---|
| Trivy (fs) | `security-trivy-fs` | build | SCA — NuGet/npm |
| Opengrep | `security-opengrep` | build | SAST (regras do pacote + `.opengrep/` do projeto) |
| Checkov | `security-checkov` | build | IaC — Dockerfile, Kubernetes, Kustomize |
| BetterLeaks | `security-betterleaks` | build | Segredos no código |
| Trivy (image) | `security-trivy-image` | **toda** pipeline | CVEs da imagem (rescan a cada promoção) |
| ZAP baseline | `dast-zap` + `dast-gate` | pós-deploy, qa/hml | DAST passivo |

**Gate** (`security-gate`, imagem `tools/security-scanner`, comando `aggregate`): achado em `SECURITY_FAIL_ON` **bloqueia em qa/hml/produção** e **só avisa em dev** (job laranja). Relatório consolidado JSON + PDF como artifact ("Relatorio de seguranca" na MR/pipeline). O DAST roda depois do deploy: não impede aquele deploy, deixa a pipeline vermelha antes da próxima promoção.

- **Falso positivo**: `.security-ignore` na raiz do projeto ([exemplo](examples/.security-ignore)) — `ruleId` ou `ferramenta:ruleId`, sempre com motivo. Continua no relatório, não conta pro bloqueio. No Opengrep o ruleId vem com o caminho (`opt.opengrep-rules.csharp.lang.security.sqli.csharp-sqli`) — copie do log do gate.
- **Checkov sem API key não informa severidade**: os achados entram como `UNKNOWN` e não bloqueiam — são informativos.
- **Severidade normalizada**: `ERROR` do Opengrep conta como HIGH; segredo do BetterLeaks é sempre HIGH.

Scan local, fora da esteira (mesmas ferramentas e versões, via Docker), a partir da raiz deste repo:

```
dotnet run --project tools/security-scanner/src/SecurityScanner.Cli -- scan --target-path C:\caminho\projeto --export json,pdf
```

## Gate de code review (opt-in)

Com `REVIEW_GATE_ENABLED: "true"` no projeto, push em `feature/*` roda só o stage `review`: `verify-code-review` exige `.code-review-ok.json` na raiz com `"commit"` igual ao sha do push, ou ao pai dele quando o push só adiciona a prova (prova do `/code-review` local — nenhuma IA roda no CI) e, se bater, `abrir-mr-dev` abre a MR `feature/* → dev`. Precisa de `GITLAB_TOKEN` (project access token, escopo `api`, mascarado) — o `CI_JOB_TOKEN` não cria MR.

## Fechar a fatia no merge (opt-in)

Com `FECHAR_FATIA_ENABLED: "true"`, o push na `dev` (o merge de uma MR) roda só o job `fechar-fatia` (`review/fechar-fatia.yml`, nenhuma IA): acha o código da fatia na MR mergeada (branch `feature/b2-…` ou título `B2 — …`), espera todas as MRs da fatia nos repositórios de `SLICE_REPOS` estarem mergeadas e então comenta na issue-mãe, fecha as filhas e a mãe e tira o label `em-execucao`. Variáveis do projeto: `WORKSPACE_PROJECT` (caminho do projeto das issues) e `SLICE_REPOS` (repositórios de código, separados por espaço). O `GITLAB_TOKEN` precisa enxergar **todos** esses projetos — use um *group access token* (escopo `api`, Developer) mascarado no grupo; o token de projeto do gate de review não basta. O job é `allow_failure`: se falhar, o `/implement-task` seguinte fecha a fatia ("Fechar a fatia" em `docs/agents/orchestration.md` do ai-base).

## Setup único

1. **Runner self-hosted** (Docker executor, **`privileged = true`** — o job de imagem usa `docker:dind`), tag `self-hosted`, sem "run untagged jobs". Roda na máquina local, enxerga o k3d e não gasta minuto do gitlab.com.
2. **GitLab Agent** por cluster (`.gitlab/agents/<nome>/config.yaml` no repo do agent) com `ci_access` liberando os projetos — é o que injeta o `KUBE_CONTEXT`.
3. **Pull de imagem no cluster**: deploy token do projeto (`read_registry`) → `kubectl create secret docker-registry gitlab-registry ...` em cada namespace (os exemplos usam `imagePullSecrets: gitlab-registry`).
4. **Secret do banco** por namespace: `kubectl create secret generic <projeto>-db --from-literal=connection=...` (migrations e app usam o mesmo).
5. **Acesso às imagens do template** (`security-scanner`, `opengrep`): em template-ci → Settings → CI/CD → Job token permissions, liberar os projetos consumidores (ou o namespace inteiro) — ou deixar o projeto template-ci público.

## Publicar uma versão do template

1. Trocar a versão em **todas** as referências: `ref: vX.Y.Z` nos `include` e o default de `SECURITY_SCANNER_IMAGE`/`OPENGREP_IMAGE` (`grep -rn "v2.0.0" --include=*.yml .`).
2. Commit em `main`, tag `vX.Y.Z`, push da tag.
3. O pipeline do próprio repo (`.gitlab-ci.yml`) confere que as referências batem com a tag e publica `security-scanner` e `opengrep` no registry.
4. Projetos atualizam o `ref` quando decidirem.

## Estrutura

```
ci-template.yml / ci-execucao.yml   pai e filho
common/        controlar-pipeline (fluxo de tag + definir-kind), rules
review/        gate de code review em feature/* e fechamento da fatia no merge em dev (opt-in)
build/ test/   .NET e Angular
image/         build/retag/resolve no registry
security/      trivy, opengrep, checkov, betterleaks, gate, zap
deploy/        kubernetes (migrations + kustomize + rollout)
verify/        smoke-test
tools/         security-scanner (CLI .NET: scan local + aggregate/gate) e opengrep (imagem)
examples/      consumo .NET, Angular e monorepo, nginx, kustomize, .security-ignore
docs/          fluxo de tags
```

## Pendências

1. **report-api** (plataforma própria do fluxo de deploy): o gate já gera `consolidated.json`; falta definir o contrato e o job de envio.
2. **Primeiro teste ponta a ponta no GitLab real**: as imagens e scripts foram validados localmente (Docker), o encadeamento de pipelines (trigger, dotenv entre jobs, variáveis com escopo de ambiente, agent) ainda não rodou no gitlab.com.
3. **ZAP contra k3d local**: o runner precisa resolver a `APP_URL` do ambiente (DNS/hosts do container do runner).
