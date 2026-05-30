# Automaton Studio Vue.js

Modern Vue.js portal for Automaton - AI-first workflow automation platform.

## 📚 Documentation

- **[REDESIGN_PLAN.md](./REDESIGN_PLAN.md)** - Complete Vue.js redesign specification (3,300+ lines)
- **[AI_INTEGRATION_PLAN.md](./AI_INTEGRATION_PLAN.md)** - AI architecture and implementation plan (1,095 lines)
- **[AI_INTEGRATION_SUMMARY.md](./AI_INTEGRATION_SUMMARY.md)** - AI quick reference guide (383 lines)

## 🏗️ Architecture

**Tech Stack:**
- Vue 3 (Composition API) + TypeScript
- PrimeVue (UI components)
- Pinia (state management)
- Vite (build tool)
- Axios (HTTP client)

**Key Features:**
- Feature-based structure (see REDESIGN_PLAN.md)
- AI-powered code generation for Steps, Apps, and Flows
- Real-time streaming AI responses (Server-Sent Events)
- Monaco Editor integration
- Dark mode support
- Mobile responsive

## 🚀 Quick Start

### Prerequisites
- Node.js 18+
- npm or pnpm

### Installation

```sh
npm install
```

### Development

```sh
npm run dev        # Start dev server (http://localhost:5173)
npm run build      # Production build
npm run preview    # Preview production build
```

### Code Quality

```sh
npm run type-check # TypeScript checks
npm run lint       # ESLint
npm run format     # Prettier
```

## 📂 Project Structure

```
src/
├── features/              # Feature modules
│   ├── auth/              # Authentication
│   ├── dashboard/         # Dashboard
│   ├── flows/             # Flow management + AI generation
│   ├── apps/              # App editor + AI assistant
│   ├── steps/             # Step editor + AI assistant
│   ├── runners/           # Runner management
│   ├── projects/          # Project organization
│   └── settings/          # User settings
│
├── shared/                # Shared utilities
│   ├── components/        # Reusable components (AI, layout, UI)
│   ├── composables/       # Vue composables (AI, theme, etc.)
│   ├── services/          # API services (HTTP, AI, SignalR)
│   ├── stores/            # Pinia stores
│   ├── types/             # TypeScript types
│   └── utils/             # Helper functions
│
├── router/                # Vue Router configuration
├── assets/                # Styles, images
├── App.vue                # Root component
└── main.ts                # Entry point
```

## 🤖 AI Integration

The Vue.js portal includes AI assistants for:

1. **Step AI Assistant** - Generate Python/PowerShell/Batch code
2. **App AI Assistant** - Generate frontend (HTML/CSS/JS) + backend
3. **Flow AI Assistant** - Natural language flow generation (NEW)

See [AI_INTEGRATION_PLAN.md](./AI_INTEGRATION_PLAN.md) for details.

## 🔧 Configuration

### Environment Variables

**`.env.development`** (local dev):
```env
VITE_API_BASE_URL=http://localhost:7091
VITE_APP_TITLE=Automaton Studio (Dev)
VITE_TURNSTILE_SITE_KEY=1x00000000000000000000AA
```

**`.env.production`**:
```env
VITE_API_BASE_URL=https://api.automaton.monster
VITE_APP_TITLE=Automaton Studio
VITE_TURNSTILE_SITE_KEY=your-production-turnstile-site-key
```

`VITE_TURNSTILE_SITE_KEY` is optional. Leave it unset to hide Turnstile in environments where you do not want bot protection. The backend secret key belongs in `CloudflareTurnstile:SecretKey` on the API side and should come from Cloudflare Turnstile widget settings, not from source control.

### Turnstile Local Testing

For local development, prefer Cloudflare's official test keys instead of a real production widget:

```env
VITE_TURNSTILE_SITE_KEY=1x00000000000000000000AA
```

```json
"CloudflareTurnstile": {
  "Enabled": true,
  "AllowBypassWhenNotConfiguredInDevelopment": true,
  "SiteKey": "1x00000000000000000000AA",
  "SecretKey": "1x0000000000000000000000000000000AA"
}
```

Notes:

- the test widget intentionally renders a visible `For testing only` banner
- test keys work on `localhost` and other local dev hostnames
- real production widgets often fail locally unless `localhost` is added to the widget hostname allowlist
- never use the test keys in production

## 🧪 Testing

```sh
npm run test       # Run all tests
npm run test:unit  # Unit tests
npm run test:e2e   # E2E tests (Playwright)
```

## 📖 Development Guides

- **Feature Modules:** See REDESIGN_PLAN.md Section 6 (Project Structure)
- **AI Integration:** See AI_INTEGRATION_PLAN.md
- **UI/UX Design:** See REDESIGN_PLAN.md Section 7 (UI/UX Redesign)
- **API Integration:** See REDESIGN_PLAN.md Section 5 (Backend API Design)

## 🛠️ Recommended IDE Setup

- [VSCode](https://code.visualstudio.com/)
- [Volar](https://marketplace.visualstudio.com/items?itemName=Vue.volar) (disable Vetur)
- [TypeScript Vue Plugin](https://marketplace.visualstudio.com/items?itemName=Vue.vscode-typescript-vue-plugin)

## 📦 Key Dependencies

- **vue** ^3.4 - Progressive JavaScript framework
- **typescript** ^5.0 - Type-safe development
- **primevue** ^4.0 - Enterprise UI components
- **pinia** ^2.1 - State management
- **vue-router** ^4.0 - Routing
- **axios** ^1.6 - HTTP client
- **CodeMirror 6** - Code editor

## 🔐 Security

- JWT authentication (all API calls)
- API keys encrypted in backend
- Rate limiting
- CORS configured
- Sanitized AI-generated code

## 🚢 Deployment

See REDESIGN_PLAN.md Section "Deployment & Testing Strategy" for:
- GitHub Pages preview deployment
- Production deployment
- Environment configuration
- CI/CD setup

## 📝 License

See root LICENSE file.

## 🤝 Contributing

This is a redesign project. See REDESIGN_PLAN.md for implementation phases and guidelines.
