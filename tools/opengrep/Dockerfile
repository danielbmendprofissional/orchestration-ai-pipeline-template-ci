# Opengrep (fork aberto do Semgrep CE) + pacote de regras, em versões fixas.
# Não existe imagem oficial. Publicada pelo pipeline do próprio template-ci:
# $CI_REGISTRY_IMAGE/opengrep:<tag>
#
# Regras: opengrep/opengrep-rules — fork das regras comunitárias do Semgrep
# (dez/2024), arquivado, ou seja, congelado. Regras próprias de cada projeto
# entram via pasta .opengrep/ no repositório (o job aplica automaticamente).
FROM debian:trixie-slim

ARG OPENGREP_VERSION=v1.31.0
ARG OPENGREP_RULES_REF=main

# Sem UTF-8 o opengrep quebra lendo regras com caractere não-ASCII (UnicodeDecodeError).
ENV LANG=C.UTF-8 \
    LC_ALL=C.UTF-8

RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl git \
    && curl -fsSL -o /usr/local/bin/opengrep \
       "https://github.com/opengrep/opengrep/releases/download/${OPENGREP_VERSION}/opengrep_manylinux_x86" \
    && chmod +x /usr/local/bin/opengrep \
    # o binário se auto-extrai em ~/.cache na primeira execução — faz isso no build, não a cada job
    && opengrep --version \
    && git clone --depth 1 --branch "${OPENGREP_RULES_REF}" https://github.com/opengrep/opengrep-rules /opt/opengrep-rules \
    && cd /opt/opengrep-rules \
    && rm -rf .git .github scripts stats \
    # só arquivos de regra: os alvos de teste do repositório (inclusive *.test.yaml) quebrariam o --config
    && find . -type f ! -name '*.yaml' ! -name '*.yml' -delete \
    && find . -type f \( -name '*.test.yaml' -o -name '*.test.yml' \) -delete \
    && git config --system --add safe.directory '*' \
    && apt-get purge -y curl \
    && apt-get autoremove -y \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /src
CMD ["opengrep", "--version"]
