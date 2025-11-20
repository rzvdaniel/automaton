# Docker Setup for Automaton Development

This directory contains Docker configurations for setting up a MySQL database for Automaton development.

## Quick Start

### Automated Test

To verify the complete setup works correctly, run the integration test:
```bash
bash docker/test-setup.sh
```

This will:
- Check prerequisites (Docker, Docker Compose, .NET SDK)
- Validate configuration files
- Start MySQL container
- Verify database and user creation
- Apply EF migrations
- Verify schema creation
- Clean up test environment

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

### 3. Start the MySQL Database

From the project root directory:
```bash
docker compose -f docker-compose.dev.yml up -d mysql
```

This will:
- Pull the MySQL 8.0 image
- Create a database named `automaton` (or your configured name)
- Create a user with the configured credentials
- Start MySQL on port 3306 (or your configured port)
- Persist data in a Docker volume

### 4. Configure Automaton.Studio.Server

Update your `appsettings.Development.json` or use .NET user secrets:

**Option A: appsettings.Development.json**
```json
{
  "ConnectionStrings": {
    "MySqlConnection": "Server=localhost;Port=3306;Database=automaton;User=automaton_user;Password=AutomatonPass123!;"
  }
}
```

**Option B: User Secrets (Recommended)**
```bash
cd src/Automaton.Studio.Server
dotnet user-secrets set "ConnectionStrings:MySqlConnection" "Server=localhost;Port=3306;Database=automaton;User=automaton_user;Password=AutomatonPass123!;"
```

### 5. Run Database Migrations

From the project root:
```bash
cd src/Automaton.Studio.Server
dotnet ef database update
```

This will apply all Entity Framework migrations to create the database schema.

### 6. Run the Server

```bash
cd src/Automaton.Studio.Server
dotnet run
```

## Useful Commands

### Check MySQL Logs
```bash
docker logs automaton-mysql-dev
```

### Connect to MySQL CLI
```bash
docker exec -it automaton-mysql-dev mysql -u automaton_user -p
# Enter password: AutomatonPass123! (or your configured password)
```

### Stop the Database
```bash
docker compose -f docker-compose.dev.yml down
```

### Stop and Remove Data (fresh start)
```bash
docker compose -f docker-compose.dev.yml down -v
```

### Backup the Database
```bash
docker exec automaton-mysql-dev mysqldump -u root -pAutomatonRoot123! automaton > backup.sql
```

### Restore from Backup
```bash
docker exec -i automaton-mysql-dev mysql -u root -pAutomatonRoot123! automaton < backup.sql
```

## Directory Structure

```
docker/
├── mysql/
│   ├── conf.d/         # MySQL configuration files
│   │   └── custom.cnf  # Custom MySQL settings
│   └── init/           # Initialization scripts (run on first start)
│       └── 01-init.sql # Database initialization
└── README.md           # This file
```

## Custom Configuration

### MySQL Configuration
Edit `docker/mysql/conf.d/custom.cnf` to customize MySQL settings.

### Initialization Scripts
Add `.sql` or `.sh` files to `docker/mysql/init/` to run custom initialization.
These run only on the first container start (when data volume is empty).

## Private Docker Registry Setup

To keep your Docker images private for your team:

### Option 1: GitHub Container Registry (Recommended for GitHub teams)

1. **Build and tag your image:**
   ```bash
   docker build -t ghcr.io/your-org/automaton-mysql:latest -f docker/Dockerfile.mysql .
   ```

2. **Authenticate with GitHub:**
   ```bash
   echo $GITHUB_TOKEN | docker login ghcr.io -u YOUR_USERNAME --password-stdin
   ```

3. **Push to GitHub Container Registry:**
   ```bash
   docker push ghcr.io/your-org/automaton-mysql:latest
   ```

4. **Team members pull the image:**
   ```bash
   docker pull ghcr.io/your-org/automaton-mysql:latest
   ```

### Option 2: Docker Hub Private Repository

1. **Create a private repository on Docker Hub**

2. **Build and tag:**
   ```bash
   docker build -t your-username/automaton-mysql:latest -f docker/Dockerfile.mysql .
   ```

3. **Login and push:**
   ```bash
   docker login
   docker push your-username/automaton-mysql:latest
   ```

4. **Grant access to team members** through Docker Hub settings

### Option 3: Self-Hosted Registry

Set up a private Docker registry on your infrastructure:
```bash
docker run -d -p 5000:5000 --name registry registry:2
docker tag automaton-mysql:latest localhost:5000/automaton-mysql:latest
docker push localhost:5000/automaton-mysql:latest
```

## Troubleshooting

### Connection Refused
- Ensure MySQL container is running: `docker ps`
- Check if port 3306 is available: `netstat -an | grep 3306`
- Verify connection string has correct host (localhost when connecting from host machine)

### Access Denied
- Verify credentials in `.env` match those in connection string
- Ensure user has proper privileges (check init script)

### Container Won't Start
- Check logs: `docker logs automaton-mysql-dev`
- Verify port 3306 is not already in use
- Remove volume and start fresh: `docker compose -f docker-compose.dev.yml down -v`

### Migrations Fail
- Ensure MySQL is fully started (wait ~30 seconds after `docker-compose up`)
- Verify connection string is correct
- Check MySQL logs for errors

## Production Considerations

This setup is for **development only**. For production:
- Use strong, randomly generated passwords
- Enable SSL/TLS connections
- Configure proper backup strategies
- Implement monitoring and alerting
- Consider managed database services (AWS RDS, Azure Database for MySQL, etc.)
- Use secrets management (Azure Key Vault, AWS Secrets Manager, HashiCorp Vault)
