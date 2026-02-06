# Git Hooks (Husky)

This directory contains Git hooks managed by Husky to ensure code quality before commits.

## Pre-commit Hook

**What it does:** Runs TypeScript type checking before every commit.

**Command:** `npm run type-check`

**Why:** Catches TypeScript errors early, ensuring build consistency between `npm run dev` and `npm run build`.

## Setup

Husky is automatically initialized when you run `npm install` (via the `prepare` script in package.json).

## Bypassing Hooks (Not Recommended)

If you absolutely need to skip hooks:
```bash
git commit --no-verify -m "your message"
```

**Note:** This is strongly discouraged. If type-check fails, fix the errors instead of bypassing.

## Troubleshooting

**Hook doesn't run:**
```bash
npx husky install
```

**Type-check fails:**
```bash
npm run type-check
# Fix the reported errors
```
