# One image of both halves: the host serves the built front end on the same origin as /api.
# Build and tag it by commit: docker build -t ai-hub:$(git rev-parse --short HEAD) .

FROM node:22-alpine AS frontend
WORKDIR /src
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
WORKDIR /src
COPY backend/ ./
RUN dotnet publish src/App/App.Api.csproj -c Release -o /publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=backend /publish ./
COPY --from=frontend /src/dist ./wwwroot
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "App.Api.dll"]
