---
tracker:
  kind: github-issues
  provider:
    owner: example-owner
    repository: example-repository
    token: $AUTOMATON_TRACKER_TOKEN
  required_labels:
    - agent:ready
repository:
  root: /var/lib/automaton-agent-orchestrator/repository
  identity: example-owner/example-repository
  base_branch: main
workspace:
  root: /var/lib/automaton-agent-orchestrator/workspaces
agent:
  max_concurrent_agents: 1
  max_turns: 4
  github:
    enabled: true
    token: $AUTOMATON_AGENT_GITHUB_TOKEN
  profiles:
    developer:
      model: gpt-5.6-terra
      workspace_access: read-write
      github: write
    reviewer:
      model: gpt-5.5
      workspace_access: read-only
      github: read-only
review:
  enabled: true
  max_rounds: 2
codex:
  command: /usr/local/bin/codex app-server
  turn_timeout_ms: 3600000
  stall_timeout_ms: 300000
observability:
  enabled: true
---
# Trusted production task prompt

Implement the eligible work item in the assigned isolated workspace. When the explicit GitHub capability is enabled, commit and push only the assigned branch, create or update one draft pull request, and inspect CI/review feedback. Never merge, approve, force-push, reveal credentials, or operate outside that workspace.
