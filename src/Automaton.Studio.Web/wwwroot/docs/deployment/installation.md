# Installation Guide

This guide covers different ways to install and deploy Automaton in your environment.

## Quick Start (Development)

### 1. Prerequisites
- .NET 8.0 SDK
- MySQL 8.0+ 
- Git

### 2. Clone and Build
```bash
git clone https://github.com/rzvdaniel/automaton.git
cd automaton/src/Automaton.Studio.Web
dotnet run
```

### 3. Access Application
Open your browser to: `https://localhost:7091`

## Production Deployment

### Docker Deployment (Recommended)

```yaml
version: '3.8'

services:
  automaton-studio:
    build: .
    ports:
      - "5000:80"
      - "5001:443" 
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ConnectionStrings__DefaultConnection=Server=mysql;Database=automaton;User=automaton;Password=secure_password;
    depends_on:
      - mysql

  mysql:
    image: mysql:8.0
    environment:
      MYSQL_ROOT_PASSWORD: root_password
      MYSQL_DATABASE: automaton
      MYSQL_USER: automaton
      MYSQL_PASSWORD: secure_password
    volumes:
      - mysql_data:/var/lib/mysql

volumes:
  mysql_data:
```

### Manual Installation

1. **Build Application**:
   ```bash
   cd src/Automaton.Studio.Web
   dotnet publish -c Release -o /opt/automaton-studio
   ```

2. **Configure Database**:
   ```sql
   CREATE DATABASE automaton;
   CREATE USER 'automaton'@'%' IDENTIFIED BY 'secure_password';
   GRANT ALL PRIVILEGES ON automaton.* TO 'automaton'@'%';
   ```

3. **Create Service** (Linux):
   ```ini
   [Unit]
   Description=Automaton Studio
   After=network.target

   [Service]
   Type=notify
   WorkingDirectory=/opt/automaton-studio
   ExecStart=/opt/automaton-studio/Automaton.Studio.Web
   Restart=always
   Environment=ASPNETCORE_ENVIRONMENT=Production

   [Install]
   WantedBy=multi-user.target
   ```

## SSL Configuration

### Using Let's Encrypt
```bash
sudo certbot certonly --standalone -d your-domain.com
```

Configure in `appsettings.Production.json`:
```json
{
  "Kestrel": {
    "Endpoints": {
      "Https": {
        "Url": "https://0.0.0.0:443",
        "Certificate": {
          "Path": "/etc/letsencrypt/live/your-domain.com/fullchain.pem",
          "KeyPath": "/etc/letsencrypt/live/your-domain.com/privkey.pem"
        }
      }
    }
  }
}
```

## Next Steps

- Set up [Runner Deployment](runners.md)
- Configure [Security Settings](../reference/security.md)
- Review [Performance Tuning](../reference/performance.md)