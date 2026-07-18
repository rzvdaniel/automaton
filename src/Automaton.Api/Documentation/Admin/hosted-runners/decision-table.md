---
title: Operations Decision Table
order: 90
---

# Operations Decision Table

| Goal | First action | Required follow-up |
| --- | --- | --- |
| Add a new runner size | Create a Specification | Assign it to a Plan |
| Add a paid tier | Create a Plan | Map an active Specification |
| Add server capacity | Add a Runner Host | Install, validate, approve, and schedule it |
| Give a user a runner | Assign an eligible Plan | Provision a Runner Container |
| Resize an existing runner | Change the Plan or mapping | Explicitly Reprovision |
| Drain a server | Disable scheduling | Reprovision existing containers |
| Pause all new provisioning | Disable the global setting | Existing containers remain running |
