# System Prompt Communication Patterns

Quick reference for making AI models communicate better during operations.

## The Core Pattern

Add this section to any system prompt where you want the AI to explain its actions:

```
## COMMUNICATION STYLE:
? **ALWAYS EXPLAIN WHAT YOU'RE DOING** ?

Before EACH tool call, briefly explain your plan:
- "I'll first read the existing code to understand the current implementation..."
- "Now I'll create a backend step for the API integration..."
- "The step is ready, let me save the updated frontend code..."

After each tool result, briefly acknowledge it:
- "Got the current code. I can see it's using Tailwind CSS..."
- "The step was created successfully. Now linking it to the app..."

This helps users understand your progress during longer operations.
```

## Why This Works

### GPT Models Without This Pattern

```
User: Update the background color

[60 seconds of silence]

Tool: ? Reading code
[30 seconds of silence]

Tool: ? Saving code
[10 seconds of silence]

AI: Done! I updated the background.
```

### GPT Models With This Pattern

```
User: Update the background color

AI: I'll read the current code first to see the existing styling...

Tool: ? Reading code

AI: Got the code. I can see it uses a dark gray (#333). 
    Let me update it to a warmer cream color...

Tool: ? Saving code

AI: Done! I changed the background from dark gray to a warm 
    cream color (#f5f0e6).
```

## Pattern Variations

### For Code Generation Tasks

```
## COMMUNICATION STYLE:
Before EACH tool call, briefly explain:
- "Let me check what code exists..."
- "I'll test this implementation..."
- "Now saving the improved version..."

After each result, acknowledge:
- "The code looks good, testing now..."
- "Test passed! The output is correct."
- "I see an error, let me fix it..."
```

### For Data Processing Tasks

```
## COMMUNICATION STYLE:
Before processing, explain:
- "I'll analyze the input data first..."
- "Processing the records now..."
- "Generating the output report..."

After each step:
- "Found 150 records to process..."
- "Completed 50% of the transformation..."
- "Report generated successfully."
```

### For Research/Analysis Tasks

```
## COMMUNICATION STYLE:
Explain your approach:
- "I'll search for relevant information..."
- "Let me verify this finding..."
- "Synthesizing the results now..."

Share intermediate findings:
- "Found 3 relevant sources..."
- "The data suggests..."
- "This confirms the initial hypothesis."
```

## Model-Specific Notes

### OpenAI GPT-4/5
- Very task-focused, needs explicit instructions
- Follows the pattern well once instructed
- May still have some silent processing gaps

### Claude (Anthropic)
- Naturally more conversational
- May over-explain without constraints
- Add "Keep explanations brief" if too verbose

### Google Gemini
- Similar to GPT, follows instructions well
- Good at structured communication
- Works well with numbered steps

## Integration Points

### Where to Add the Pattern

1. **System Prompts**: Primary location
   - `UnifiedAppSystemPromptBuilder.cs`
   - `StepSystemPromptBuilder.cs`
   - `AppSystemPromptBuilder.cs`

2. **UI Components**: Display the messages
   - `ReasoningMessageComponent.razor`
   - `ChatMessageComponent.razor`

3. **Event Handlers**: Process the messages
   - `AiChatPanel.razor.cs` ? `OnConversationUpdated`

## Testing the Pattern

1. Clear conversation history
2. Send a request that requires multiple tool calls
3. Verify you see AI explanations between tool executions
4. Check that messages appear in real-time, not just at the end

## Troubleshooting

### AI Still Silent Between Tools

- Make the instructions more emphatic (? emoji helps)
- Add specific examples for your domain
- Try: "IMPORTANT: Always explain before each action"

### AI Too Verbose

- Add: "Keep explanations to 1-2 sentences"
- Remove example phrases to reduce length
- Use: "Brief acknowledgment only"

### Messages Appearing as Duplicates

- Check duplicate detection in `OnConversationUpdated`
- Adjust similarity threshold if needed
- Verify MCP client isn't emitting same message twice
