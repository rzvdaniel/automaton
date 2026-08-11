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
publication:
  validation_commands:
    - dotnet test
  github_token: $AUTOMATON_PUBLICATION_TOKEN
ci:
  enabled: true
  kind: github
  github_token: $AUTOMATON_CI_TOKEN
review_feedback:
  enabled: true
  kind: github
  github_token: $AUTOMATON_REVIEW_FEEDBACK_TOKEN
  automation_login: automaton-agent
workspace:
  root: /var/lib/automaton-agent-orchestrator/workspaces
agent:
  max_concurrent_agents: 1
codex:
  command: /usr/local/bin/codex app-server
observability:
  enabled: true
---
# Trusted production task prompt

Implement the eligible work item in the assigned isolated workspace. Do not reveal credentials or operate outside that workspace.
