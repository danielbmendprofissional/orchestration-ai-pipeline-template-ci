# Imagem do agregador/gate usado pelos jobs security-gate e dast-gate.
# Publicada pelo pipeline do próprio template-ci: $CI_REGISTRY_IMAGE/security-scanner:<tag>
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/SecurityScanner.Cli/SecurityScanner.Cli.csproj SecurityScanner.Cli/
RUN dotnet restore SecurityScanner.Cli/SecurityScanner.Cli.csproj
COPY src/ .
RUN dotnet publish SecurityScanner.Cli/SecurityScanner.Cli.csproj -c Release --no-restore -o /out

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /out .
RUN printf '#!/bin/sh\nexec dotnet /app/SecurityScanner.Cli.dll "$@"\n' > /usr/local/bin/security-scanner \
    && chmod +x /usr/local/bin/security-scanner
# Sem USER não-root de propósito: no executor Docker do GitLab o diretório do build
# pertence ao root, e o gate precisa escrever o relatório lá.
ENTRYPOINT ["security-scanner"]
