# Deployment Guide

## Current Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                    Your Subdomains                               │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  studio.automaton.monster  → Blazor UI (Studio)                 │
│  api.automaton.monster     → ASP.NET API + User App Files       │
│  www.automaton.monster     → Static marketing website           │
│                                                                  │
│  [Optional - Recommended for security]                          │
│  apps.automaton.monster    → User-generated apps (sandboxed)    │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

## Digital Ocean Deployment Options

### Option 1: App Platform (Recommended for Simplicity)

**Cost: ~$12-25/month**

Digital Ocean App Platform handles deployment, SSL, and scaling automatically.

```yaml
# .do/app.yaml
name: automaton
services:
  # API + Studio (same container)
  - name: api
    github:
      repo: rzvdaniel/automaton
      branch: main
      deploy_on_push: true
    source_dir: src/Automaton.Studio.Server
    dockerfile_path: Dockerfile
    http_port: 8080
    instance_size_slug: basic-xs  # $12/month
    instance_count: 1
    routes:
      - path: /
    envs:
      - key: ASPNETCORE_ENVIRONMENT
        value: Production
      - key: ConnectionStrings__MySqlConnection
        value: ${db.DATABASE_URL}
        type: SECRET

databases:
  - name: db
    engine: MYSQL
    version: "8"
    size: db-s-1vcpu-1gb  # $15/month
```

**Pros:**
- Automatic SSL certificates
- Automatic deployments from GitHub
- Managed database option
- Easy scaling

**Cons:**
- More expensive than Droplet
- Less control

### Option 2: Droplet (Recommended for Control/Cost)

**Cost: ~$6-12/month**

A single Droplet running all services with Docker Compose.

```yaml
# docker-compose.yml
version: '3.8'

services:
  api:
    build:
      context: .
      dockerfile: src/Automaton.Studio.Server/Dockerfile
    ports:
      - "5000:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ConnectionStrings__MySqlConnection=Server=db;Database=automaton;User=root;Password=${MYSQL_ROOT_PASSWORD}
    volumes:
      - app-data:/app/AppData  # Persist user app files
    depends_on:
      - db
    restart: unless-stopped

  db:
    image: mysql:8.0
    environment:
      - MYSQL_ROOT_PASSWORD=${MYSQL_ROOT_PASSWORD}
      - MYSQL_DATABASE=automaton
    volumes:
      - mysql-data:/var/lib/mysql
    restart: unless-stopped

  # Reverse proxy for SSL and routing
  caddy:
    image: caddy:2-alpine
    ports:
      - "80:80"
      - "443:443"
    volumes:
      - ./Caddyfile:/etc/caddy/Caddyfile
      - caddy-data:/data
    depends_on:
      - api
    restart: unless-stopped

volumes:
  app-data:
  mysql-data:
  caddy-data:
```

```
# Caddyfile (automatic SSL with Let's Encrypt)
api.automaton.monster {
    reverse_proxy api:8080
}

studio.automaton.monster {
    reverse_proxy api:8080
}

# Optional: Separate subdomain for user apps
apps.automaton.monster {
    reverse_proxy api:8080
}
```

**Droplet Setup:**
```bash
# 1. Create $6/month Droplet (1GB RAM, 25GB SSD)
# 2. SSH in and install Docker
curl -fsSL https://get.docker.com | sh

# 3. Clone your repo
git clone https://github.com/rzvdaniel/automaton.git
cd automaton

# 4. Create .env file
echo "MYSQL_ROOT_PASSWORD=your-secure-password" > .env

# 5. Start services
docker-compose up -d
```

## Subdomain Configuration for User Apps

### Why Separate Subdomain?

Serving user-generated content from a **different subdomain** prevents XSS attacks:

```
Main app cookies:  studio.automaton.monster (HttpOnly, Secure)
User app JS:       apps.automaton.monster (cannot access studio cookies!)
```

### URL Routes

| URL | Controller | Purpose |
|-----|------------|---------|
| `/api/apps/{id}/files/*` | `AppsController` | Edit files in Studio (authenticated) |
| `/api/web/{slug}` | `PublicWebAppsController` | Get app metadata + HTML (JSON response) |
| `/api/web/{slug}/files/{path}` | `PublicWebAppsController` | Get any file from published app |
| `/apps/{slug}` | `UserAppsController` | Serve raw HTML (for subdomain) |
| `/apps/{slug}/{path}` | `UserAppsController` | Serve any file (CSS, JS, images) |

### How It Works

Your API server handles all subdomains, but routes differently:

```
api.automaton.monster/api/...           → API endpoints
studio.automaton.monster/...            → Blazor UI
apps.automaton.monster/my-app           → Caddy rewrites to /apps/my-app
                                        → UserAppsController serves HTML
apps.automaton.monster/my-app/style.css → Caddy rewrites to /apps/my-app/style.css
                                        → UserAppsController serves CSS
```

### DNS Configuration

All subdomains point to the same server:

```
Type    Name      Value              TTL
A       api       YOUR_SERVER_IP     3600
A       studio    YOUR_SERVER_IP     3600
A       apps      YOUR_SERVER_IP     3600
A       www       YOUR_SERVER_IP     3600
```

### Caddy Configuration (Recommended)

Caddy automatically handles SSL certificates:

```
# Caddyfile
api.automaton.monster {
    reverse_proxy localhost:5000
}

studio.automaton.monster {
    reverse_proxy localhost:5000
}

apps.automaton.monster {
    # Security headers for user content
    header X-Content-Type-Options nosniff
    header X-Frame-Options SAMEORIGIN
    header Content-Security-Policy "default-src 'self' 'unsafe-inline' 'unsafe-eval'"

    # Rewrite /my-app to /apps/my-app for UserAppsController
    # This allows clean URLs: apps.automaton.monster/my-app
    rewrite * /apps{uri}

    reverse_proxy localhost:5000
}

www.automaton.monster {
    root * /var/www/automaton-website
    file_server
}
```

**Key point**: The `rewrite * /apps{uri}` line transforms:
- `apps.automaton.monster/my-app` → `/apps/my-app`
- `apps.automaton.monster/my-app/styles.css` → `/apps/my-app/styles.css`

This maps to the `UserAppsController` routes.

### Nginx Configuration (Alternative)

```nginx
# /etc/nginx/sites-available/automaton

# API
server {
    listen 443 ssl;
    server_name api.automaton.monster;

    ssl_certificate /etc/letsencrypt/live/automaton.monster/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/automaton.monster/privkey.pem;

    location / {
        proxy_pass http://localhost:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host $host;
        proxy_cache_bypass $http_upgrade;
    }
}

# Studio (same backend)
server {
    listen 443 ssl;
    server_name studio.automaton.monster;

    ssl_certificate /etc/letsencrypt/live/automaton.monster/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/automaton.monster/privkey.pem;

    location / {
        proxy_pass http://localhost:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
    }
}

# User Apps (sandboxed)
server {
    listen 443 ssl;
    server_name apps.automaton.monster;

    ssl_certificate /etc/letsencrypt/live/automaton.monster/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/automaton.monster/privkey.pem;

    # Security headers for user content
    add_header X-Content-Type-Options nosniff always;
    add_header X-Frame-Options SAMEORIGIN always;
    add_header Content-Security-Policy "default-src 'self' 'unsafe-inline' 'unsafe-eval'" always;

    location / {
        # Rewrite /my-app to /apps/my-app for UserAppsController
        rewrite ^/(.*)$ /apps/$1 break;
        proxy_pass http://localhost:5000;
        proxy_set_header Host $host;
    }
}
```

## File Storage Location

### Development
```
src/Automaton.Studio.Server/AppData/apps/{userId}/{appId}/
```

### Production (Docker)
```yaml
volumes:
  - /var/automaton/apps:/app/AppData/apps  # Persist outside container
```

### Production (App Platform)
App Platform doesn't persist files between deployments. Options:
1. **Use GitHub as source of truth** (recommended)
2. **Use DigitalOcean Spaces** (S3-compatible object storage)
3. **Mount a Volume** (available on App Platform)

## Adding Cloudflare (Optional but Recommended)

### Setup
1. Add domain to Cloudflare (free)
2. Update nameservers at your registrar
3. Enable "Proxied" (orange cloud) for all records

### Benefits
- Free SSL
- DDoS protection
- Caching of static files
- Analytics

### Cache Rules for User Apps
In Cloudflare Dashboard → Rules → Page Rules:

```
apps.automaton.monster/*
  Cache Level: Cache Everything
  Edge Cache TTL: 1 hour

api.automaton.monster/api/*
  Cache Level: Bypass
```

## Cost Summary

| Setup | Monthly Cost |
|-------|-------------|
| **Droplet + MySQL** | $6 + $15 = $21 |
| **Droplet + Self-hosted MySQL** | $12 (2GB RAM) |
| **App Platform** | $12 + $15 = $27 |
| **Cloudflare** | Free |

## Migration Steps

1. **Set up Digital Ocean Droplet**
   ```bash
   # Create Droplet, install Docker
   ```

2. **Configure DNS**
   ```
   Point all subdomains to new server IP
   ```

3. **Deploy with Docker Compose**
   ```bash
   docker-compose up -d
   ```

4. **Set up SSL with Caddy/Certbot**
   ```bash
   # Caddy does this automatically
   ```

5. **Migrate Database**
   ```bash
   mysqldump -h old-server automaton > backup.sql
   mysql -h new-server automaton < backup.sql
   ```

6. **Add Cloudflare (optional)**
   ```
   Change nameservers, enable proxy
   ```

## Monitoring

### Basic (Free)
- DigitalOcean Droplet metrics (CPU, RAM, Disk)
- Cloudflare analytics

### Recommended Additions
- **Uptime monitoring**: UptimeRobot (free)
- **Error tracking**: Sentry (free tier)
- **Logs**: Papertrail or DO Logs
