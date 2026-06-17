# Local Email Testing With smtp4dev

Automaton already includes a local development email capture service through `smtp4dev`.

Use this instead of `NoOpEmailService` when you want to inspect real confirmation, reset-password, and invitation emails locally.

## What It Provides

- local SMTP server for the API to send to
- browser inbox UI for viewing captured emails
- no real delivery to external providers like Gmail

Defined in:

- [docker-compose.dev.yml](/C:/Work/automaton/docker-compose.dev.yml)

Default local endpoints:

- Web UI: [http://localhost:3000](http://localhost:3000)
- SMTP: `localhost:2525`

## Start smtp4dev

```powershell
docker compose -f docker-compose.dev.yml up smtp4dev -d
```

Then open:

- [http://localhost:3000](http://localhost:3000)

## Development Configuration

In [appsettings.Development.json](/C:/Work/automaton/src/Automaton.Api/appsettings.Development.json), configure:

```json
"EmailConfig": {
  "EmailAccount": "dev@automaton.local",
  "EmailPassword": "",
  "EmailHost": "localhost",
  "EmailPort": 2525,
  "EmailUseSsl": false,
  "EmailSupport": "dev@automaton.local",
  "SendEmailsInDevelopment": true
}
```

Important:

- `SendEmailsInDevelopment` must be `true`
- if `SendEmailsInDevelopment` is `false`, the API uses `NoOpEmailService` and nothing will appear in smtp4dev
- `smtp4dev` is local-only and does not require real mailbox credentials

## How To Test

Trigger any email flow:

- register a new user
- resend confirmation
- forgot password
- reset password confirmation
- invitations

Then check:

- [http://localhost:3000](http://localhost:3000)

You should see the captured email and can open the confirmation/reset links directly from the UI.

## How To Tell Which Email Mode Is Active

`EmailService` active:

- emails appear in smtp4dev
- API logs show successful email send messages

`NoOpEmailService` active:

- requests succeed, but no email appears in smtp4dev
- useful only for smoke testing the code path, not for inspecting email contents

## Recommendation

For local auth-flow testing:

- use `smtp4dev`

Use `NoOpEmailService` only when you want email-related requests to succeed without running any SMTP service.
