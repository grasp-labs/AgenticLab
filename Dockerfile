# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
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

FROM build AS publish-a2aserver
RUN dotnet publish src/AgenticLab.A2AServer/AgenticLab.A2AServer.csproj \
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

FROM runtime AS a2aserver
COPY --from=publish-a2aserver /out .
ENTRYPOINT ["dotnet", "AgenticLab.A2AServer.dll"]
