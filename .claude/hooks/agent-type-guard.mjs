// PreToolUse hook for the Agent tool: issue and PR work must go to the agent types that carry the delivery rules.
let input = '';
process.stdin.on('data', chunk => { input += chunk; });
process.stdin.on('end', () => {
  let toolInput = {};
  try { toolInput = JSON.parse(input).tool_input ?? {}; } catch { process.exit(0); }
  const type = toolInput.subagent_type ?? 'general-purpose';
  const prompt = String(toolInput.prompt ?? '');
  const generic = ['general-purpose', 'claude'].includes(type);
  const deliveryWork = /(#\d{3,}|\bPR\b|\bpull request\b)/i.test(prompt)
    && /\b(implement|review|security pass|merge gate)\b/i.test(prompt);
  if (generic && deliveryWork) {
    process.stderr.write('Issue or PR work goes to subagent_type "automaton-implementer" (implementation) or '
      + '"automaton-reviewer" (review, security pass); they carry AGENTS.md and the delivery skills. '
      + 'Brief it with the issue, lane, known facts and whether it may merge.\n');
    process.exit(2);
  }
  process.exit(0);
});
