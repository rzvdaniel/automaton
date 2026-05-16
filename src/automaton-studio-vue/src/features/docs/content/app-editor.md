# Workflow: App Editor

Use the App Editor when you want the fastest in-browser start. No terminal, no CLI, no local project required.

## Best for

- Quick prototypes and MVPs
- Non-developers exploring ideas
- Fast iteration without leaving the browser

## Flow

### 1. Add your AI API key (required before anything else)

Go to **Settings → AI Keys → Add Key** and enter a key for your preferred provider (OpenAI, Anthropic, Google Gemini, or another supported provider). The AI chat will not respond until at least one key is saved.

### 2. Create an app

Go to **Apps → New App**, give it a name, then open it in the App Editor.

### 3. Describe what you want to build

Type a prompt in the AI chat panel. The AI writes both the frontend and backend files at once. Example:

```
Build a Poker Planning app where users vote on story complexity.
The frontend should show a list of stories and a set of voting cards.
```

The AI knows the Automaton app structure automatically through the built-in guidance. You do not need to explain `handle()`, `callController()`, or the file layout.

### 4. Review and iterate

Browse the generated files in the editor. Ask follow-up questions to refine:

```
Add a "reveal votes" button that shows everyone's selection at once.
```

```
Change the card values to the Fibonacci sequence.
```

### 5. Deploy the backend

Click **Deploy Backend** any time Python files in `backend/` change. You do not need to deploy for frontend-only edits — those take effect in the preview immediately.

### 6. Publish

Click **Publish** in the Settings tab to get a public URL you can share. Toggle between Draft and Published at any time.

## When to switch to the CLI workflow

- When the app grows beyond a few files
- When you want Git history and version control
- When you want to use your own editor (Cursor, VS Code, etc.)
- When you want to work offline

Export the project via the App Editor or use `automaton pull` to get a local copy, then continue with `automaton deploy`.
