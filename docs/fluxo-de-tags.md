# Fluxo de tags e promoção

Regras que o `common/controlar-pipeline.yml` impõe. Branches padronizados: **`dev` → `qa` → `hml` → `main`** (sem aliases — outro nome de branch não é reconhecido como estágio).

## 1. Regras gerais

- **Toda tag segue `vX.Y.Z-rcN` (candidata) ou `vX.Y.Z` (final).** Outro formato bloqueia a pipeline.
- **`MINOR` e `PATCH` ficam `0` durante a criação de uma candidata.** Só mudam depois de publicado, em produção (seção 3): `MINOR` vira `minor`, `PATCH` vira `hotfix`. Candidata com `MINOR`/`PATCH` diferente de `0` é bloqueada.
- **Uma candidata pode ser criada em `dev`, `qa` ou `hml`** — a tag precisa estar na ponta de um desses branches quando é cortada. Merges de sincronização por cima são tolerados; **commit de conteúdo real** entre a tag e o topo do branch conta como "tag desatualizada" e é rejeitado. Exceção: a `-rc1` de um `MAJOR` novo só nasce no primeiro estágio que existir (seção 2.1).
- **Tag final (sem `-rcN`) só em `main`**, e só publica se o conteúdo for idêntico ao da última candidata validada do mesmo core (seção 3).
- **Promoção (qa → hml → produção) é sempre `git merge <tag>`** — mesclando a tag, não o branch `dev` que por acaso está parado nela. A detecção de promotion depende disso.
- **Uma tag ativa por ambiente.** Merge de sincronização (ex: `git pull` depois de push rejeitado) não redispara deploy.
- **Commit direto em `qa`/`hml`/`main`** que não seja tag nem promotion bloqueia a pipeline.

## 2. Candidata (`vX.Y.Z-rcN`): a origem decide até onde a esteira reinicia

O `controlar-pipeline` não avalia o tamanho da mudança — só prova **de onde o commit da tag nasceu** (ancestralidade contra a ponta de `dev`, `qa` e `hml`; empate fica com o mais upstream):

| Origem | Significado | Efeito |
|---|---|---|
| `dev` | Candidata nova ou correção que precisou de contexto amplo | Reinicia a esteira: qa → hml |
| `qa` | Correção direto sobre o `qa` atual | Pula o `dev`, revalida a partir do qa |
| `hml` | Ajuste direto sobre o `hml` atual | Revalida só no hml |

Correção que nasce fora do `dev` exige **back-merge manual** pro `dev`/`qa` (`git merge` simples, sem tag) — senão a próxima release cortada do `dev` não carrega o ajuste.

Também bloqueia: regredir `MINOR`/`PATCH` em relação à última tag do `MAJOR`; candidata que não **descende de verdade** da anterior do mesmo core (`rc2` precisa ter `rc1` como ancestral); sequência que pula número (`rc1` → `rc3`) ou que não começa em `rc1`.

### 2.1 A primeira `-rc1` de um `MAJOR` novo

1. **Nasce no primeiro estágio que existe** no remoto (`dev`; sem `dev`, `qa`; sem os dois, `hml`).
2. Vários `MAJOR`s podem estar em andamento ao mesmo tempo — coordenar fluxos concorrentes é responsabilidade de quem opera o projeto (git não isola conteúdo entre fluxos que passam pelos mesmos branches).
3. **O estágio de origem precisa estar em dia com a última tag publicada.** Se produção tem um hotfix/minor que ele nunca recebeu, mescle `main` nele antes.
4. **Precisa haver conteúdo novo** além da última tag publicada.

## 3. Tag final (`vX.Y.Z`): graduação pra produção

Produção só nasce de tag final — push de branch em `main` sem tag bloqueia com erro.

1. Mescle a candidata aprovada em `main` (MR `hml → main`, ou de `release/vX.Y.Z-rcN` — seção 6) — **não dispara deploy**.
2. Corte a tag final no mesmo commit: `git tag vX.Y.Z && git push origin vX.Y.Z`.
3. O `controlar-pipeline` confirma que a tag pertence a `main` e que o conteúdo é idêntico ao da candidata `vX.Y.Z-rcN`. **A imagem não é reconstruída**: recebe a tag `vX.Y.Z` apontando pro mesmo digest da candidata validada.
4. Sem candidata correspondente, só é aceita como evolução direta de produção a partir da última final publicada no repositório:
   - **`hotfix`**: mesmo `MAJOR.MINOR`, `PATCH` maior. Ex: `v3.3.0` → `v3.3.1`.
   - **`minor`**: mesmo `MAJOR`, `MINOR` exatamente `+1`, `PATCH` `0`. Ex: `v3.3.1` → `v3.4.0`.
   - Qualquer outra combinação bloqueia. Sem nenhuma final anterior, também bloqueia — a primeira versão sempre passa pelo fluxo completo.
   - Hotfix e minor **compilam e geram imagem nova** (não existe candidata pra reaproveitar).

O deploy em produção é sempre `when: manual` — promotion, hotfix e minor.

## 4. Hotfix e minor

1. Crie `hotfix/<algo>` a partir de `main` (nome sem significado pra pipeline).
2. Mescle em `main` — **não dispara deploy**.
3. Corte a tag final sobre o merge (`PATCH` à frente = hotfix; `MINOR` à frente com `PATCH` 0 = minor).
4. Depois de publicado, mescle `main` no `dev` (estágio de origem) — senão a próxima `-rc1` de `MAJOR` novo bloqueia. Descer pra `hml`/`qa` é opcional (cascata da seção 5).

## 5. Incidente em produção

- **Nunca `git reset --hard` em `main`.** Use `git revert -m 1 <sha-do-merge-ruim>`.
- **Versão nunca regride**: a correção vira hotfix/minor pra frente.
- **Rollback de imagem**: redeploy de uma final anterior já validada em `main` (ex: voltar de `v1.1.1` pra `v1.1.0`) é permitido em cascata, `main → hml → qa → dev`, sem pular estágio. A imagem dessa tag ainda está no registry — o deploy usa o mesmo digest.
- **O banco não volta junto.** Migrations são só pra frente; a versão anterior da app precisa funcionar com o schema novo (expand/contract).

## 6. Passo a passo

```bash
# desenvolvimento normal em dev
git checkout dev && git pull origin dev
git commit -m "feature X" && git push origin dev

# candidata (sempre a partir de dev) → build, testes, imagem, scans (gate só avisa), deploy em dev se houver cluster
git tag v1.0.0-rc1 && git push origin v1.0.0-rc1

# promoção para QA → mesma imagem, rescan da imagem, deploy em qa, smoke + ZAP
git checkout qa && git pull origin qa
git merge v1.0.0-rc1 && git push origin qa

# promoção para HML: merge da tag, ou MR qa → hml (aprovação humana)
git checkout hml && git pull origin hml
git merge v1.0.0-rc1 && git push origin hml

# produção: MR hml → main (aprovação humana), merge pela tela do GitLab — não publica
git checkout main && git pull origin main
git tag v1.0.0 && git push origin v1.0.0     # deploy manual em produção
```

**MR entre branches (`qa → hml`, `hml → main`)** funciona quando a tag está na ponta do branch de origem — a pipeline encontra a tag no merge. Se o branch de origem tiver andado além da tag (algum commit por cima), use uma **branch de release** como origem da MR: criada a partir da TAG, com o nome exato dela — `git checkout -b release/v1.0.0-rc1 v1.0.0-rc1` (a pipeline usa o nome como confirmação extra quando o histórico é inconclusivo).

## 7. Avisos

- **Apagar tag só localmente não basta**: `git push origin --delete <tag>` antes de recriar com o mesmo nome.
- **`Could not resolve host`** no `controlar-pipeline` é rede/DNS do runner — rode de novo.
- **Retry não corrige bloqueio de fluxo**: reaproveita as variáveis já calculadas. Pra reavaliar, **CI/CD → Pipelines → Run pipeline** no branch.

## 8. Quando a promoção não é detectada

Erro "não corresponde a nenhum fluxo de deploy" depois do merge da tag:

- **Opção A — pipeline nova (sem tocar no git):** quando o merge está estruturalmente correto (`git log --oneline --graph -5 <branch>` mostra o merge com a tag como segundo pai). **CI/CD → Pipelines → Run pipeline** no branch.
- **Opção B — corrigir o merge:** quando foi mesclado um branch comum em vez da tag. Em branch não protegido: `git reset --hard <antes-do-merge>`, `git push --force`, refazer `git merge <tag>` (combine com o time — reescreve histórico). Em `main` (protegido): recrie a `release/vX.Y.Z-rcN` a partir da tag e abra MR nova — mesmo com "no changes", o que vale é a estrutura do merge.
