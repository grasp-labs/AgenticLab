# syntax=docker/dockerfile:1
# Build natively and publish portable (framework-dependent, RID-less) output, so one build serves any target
# platform, e.g. linux/amd64 cluster images built on an arm64 machine.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore AgenticLab.slnx

FROM build AS publish-web
RUN dotnet publish src/AgenticLab.Web/AgenticLab.Web.csproj \
    --configuration Release \
    --output /out \
    --no-restore \
    /p:UseAppHost=false

FROM build AS publish-aiservice
RUN dotnet publish src/AgenticLab.AiService/AgenticLab.AiService.csproj \
    --configuration Release \
    --output /out \
    --no-restore \
    /p:UseAppHost=false

FROM build AS publish-mcpserver
RUN dotnet publish src/AgenticLab.McpServer/AgenticLab.McpServer.csproj \
    --configuration Release \
    --output /out \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --chmod=644 LICENSE NOTICE ./
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080
USER $APP_UID

FROM runtime AS web
COPY --from=publish-web /out .
ENTRYPOINT ["dotnet", "AgenticLab.Web.dll"]

FROM runtime AS aiservice
COPY --from=publish-aiservice /out .
ENTRYPOINT ["dotnet", "AgenticLab.AiService.dll"]

FROM runtime AS mcpserver
COPY --from=publish-mcpserver /out .
ENTRYPOINT ["dotnet", "AgenticLab.McpServer.dll"]
