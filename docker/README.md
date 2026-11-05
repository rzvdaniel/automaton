# Docker Setup for Automaton Development

This directory contains Docker configurations for local development dependencies such as PostgreSQL and RabbitMQ.

## Quick Start

### Automated Test

To verify the complete setup works correctly, run the integration test:
```bash
bash docker/test-setup.sh
```

This will:
- Check prerequisites (Docker, Docker Compose, .NET SDK)
- Validate configuration files
- Start PostgreSQL container
- Verify database and user creation
- Confirm database is ready for automatic migrations
- Clean up test environment

**Note:** Migrations are applied automatically when you first run `dotnet run`.

### Manual Setup

### 1. Prerequisites
- Docker and Docker Compose installed
- .NET 8.0 SDK (for running the server locally)

### 2. Set Up Environment Variables

Copy the example environment file:
```bash
cp .env.example .env
```

Edit `.env` with your preferred credentials (or use the defaults for local development).

### 3. Start the PostgreSQL Database

From the project root directory:
```bash
docker compose -f docker-compose.dev.yml up -d postgres
```

This will:
- Pull the PostgreSQL 16 image
- Create a database named `automaton` (or your configured name)
- Create a user with the configured credentials
- Start PostgreSQL on port 5432 (or your configured port)
- Persist data in a Docker volume

### 4. Start RabbitMQ

From the project root directory:
```bash
docker compose -f docker-compose.rabbitmq.yml up -d
```

This will:
- Pull the RabbitMQ management image
- Start the AMQP broker on port 5672 (or your configured port)
- Start the management UI on port 15672 (or your configured port)
- Persist RabbitMQ data in a Docker volume

Management UI:
```txt
http://localhost:15672
```

Default login values come from `.env`:
- username: `automaton`
- password: the `RABBITMQ_PASSWORD` value from `.env`

### 5. Configure Automaton.Api

Update your `appsettings.Development.json` or use .NET user secrets:

**Option A: appsettings.Development.json**
```json
{
  "ConnectionStrings": {
    "PostgreSqlConnection": "Host=localhost;Port=5432;Database=automaton;Username=automaton_user;Password=<your-POSTGRES_PASSWORD>"
  }
}
```

**Option B: User Secrets (Recommended)**
```bash
cd src/Automaton.Api
dotnet user-secrets set "ConnectionStrings:PostgreSqlConnection" "Host=localhost;Port=5432;Database=automaton;Username=automaton_user;Password=<your-POSTGRES_PASSWORD>"
dotnet user-secrets set "RabbitMq:Host" "localhost"
dotnet user-secrets set "RabbitMq:Username" "automaton"
dotnet user-secrets set "RabbitMq:Password" "<your-RABBITMQ_PASSWORD>"
```

### 6. Start the Server

Database migrations are applied automatically when the server starts for the first time:

```bash
cd src/Automaton.Api
dotnet run
```

The server will automatically:
- Connect to the PostgreSQL database
- Apply all pending Entity Framework migrations
- Create the database schema (38 tables)
- Connect to RabbitMQ using the configured credentials
- Start the Automaton Studio Server

## Useful Commands

### Check PostgreSQL Logs
```bash
docker logs automaton-postgres
```

### Connect with psql
```bash
docker exec -it automaton-postgres psql -U automaton_user -d automaton
```

### Check RabbitMQ Logs
```bash
docker logs automaton-rabbitmq
```

### Stop the Database
```bash
docker compose -f docker-compose.postgres.yml down
docker compose -f docker-compose.rabbitmq.yml down
```

### Stop and Remove Data (fresh start)
```bash
docker compose -f docker-compose.postgres.yml down -v
docker compose -f docker-compose.rabbitmq.yml down -v
```

### Backup the Database
```bash
docker exec automaton-postgres pg_dump -U automaton_user -Fc automaton > backup.dump
```

### Restore from Backup
```bash
docker exec -i automaton-postgres pg_restore -U automaton_user -d automaton --clean --if-exists < backup.dump
```

## Directory Structure

```
docker/
├── ../docker-compose.postgres.yml
├── ../docker-compose.rabbitmq.yml
└── README.md           # This file
```

## Private Docker Registry Setup

To keep your Docker images private for your team:

### Option 1: GitHub Container Registry (Recommended for GitHub teams)

1. **Build and tag your image:**
   ```bash
   docker build -t ghcr.io/your-org/automaton-postgres:latest -f docker/Dockerfile.postgres .
   ```

2. **Authenticate with GitHub:**
   ```bash
   echo $GITHUB_TOKEN | docker login ghcr.io -u YOUR_USERNAME --password-stdin
   ```

3. **Push to GitHub Container Registry:**
   ```bash
   docker push ghcr.io/your-org/automaton-postgres:latest
   ```

4. **Team members pull the image:**
   ```bash
   docker pull ghcr.io/your-org/automaton-postgres:latest
   ```

### Option 2: Docker Hub Private Repository

1. **Create a private repository on Docker Hub**

2. **Build and tag:**
   ```bash
   docker build -t your-username/automaton-postgres:latest -f docker/Dockerfile.postgres .
   ```

3. **Login and push:**
   ```bash
   docker login
   docker push your-username/automaton-postgres:latest
   ```

4. **Grant access to team members** through Docker Hub settings

### Option 3: Self-Hosted Registry

Set up a private Docker registry on your infrastructure:
```bash
docker run -d -p 5000:5000 --name registry registry:2
docker tag automaton-postgres:latest localhost:5000/automaton-postgres:latest
docker push localhost:5000/automaton-postgres:latest
```

## Troubleshooting

### Connection Refused
- Ensure the PostgreSQL container is running: `docker ps`
- Check if port 5432 is available: `netstat -an | grep 5432`
- Verify connection string has correct host (localhost when connecting from host machine)
- Ensure RabbitMQ container is running if the API or Worker uses MassTransit

### Access Denied
- Verify credentials in `.env` match those in connection string
- The migration creates the `citext` extension; the database user must own the database (the container user does)
- Verify `RABBITMQ_USER` and `RABBITMQ_PASSWORD` in `.env` match the app config

### Container Won't Start
- Check logs: `docker logs automaton-postgres-dev`
- Verify port 5432 is not already in use
- Remove volume and start fresh: `docker compose -f docker-compose.dev.yml down -v`

### Migrations Fail
- Ensure PostgreSQL is fully started (wait ~10 seconds after `docker-compose up`)
- Verify connection string is correct
- Check PostgreSQL logs for errors

## Production Considerations

This setup is for **development only**. For production:
- Use strong, randomly generated passwords
- Enable SSL/TLS connections
- Configure proper backup strategies
- Implement monitoring and alerting
- Consider managed database services (AWS RDS, Azure Database for PostgreSQL, etc.)
- Use secrets management (Azure Key Vault, AWS Secrets Manager, HashiCorp Vault)
