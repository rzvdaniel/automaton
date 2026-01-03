# AI Chat Panel - Stop Conversation UI

## Normal State (Before Generation)

```
┌─────────────────────────────────────────────────────┐
│ AI Chat Panel                                       │
├─────────────────────────────────────────────────────┤
│                                                     │
│ System: AI can now see and test your code...       │
│                                                     │
│ User: Write a Python function to calculate...      │
│                                                     │
│ AI: Here's a Python function that calculates...    │
│                                                     │
├─────────────────────────────────────────────────────┤
│ ┌─────────────────────────────────────────────┐   │
│ │ Type your message here...                   │   │
│ │                                             │   │
│ └─────────────────────────────────────────────┘   │
│                                                     │
│ [Ask ▾] [gpt-4 ▾]                         [►]     │
│                                                     │
└─────────────────────────────────────────────────────┘
```

## Generating State (With Stop Button)

```
┌─────────────────────────────────────────────────────┐
│ AI Chat Panel                                       │
├─────────────────────────────────────────────────────┤
│                                                     │
│ User: Create a sorting algorithm                   │
│                                                     │
│ AI: [Thinking...]                                  │
│ ⚙️ Reading code ✓ Success                          │
│ ⚙️ Analyzing step variables ⏳ Executing...        │
│                                                     │
│ AI: I'm analyzing your requirements and will       │
│     create an efficient sorting algorithm...       │
│     [Streaming...]                                 │
│                                                     │
├─────────────────────────────────────────────────────┤
│ ┌─────────────────────────────────────────────┐   │
│ │ [Input disabled during generation]          │   │
│ │                                             │   │
│ └─────────────────────────────────────────────┘   │
│                                                     │
│ [Ask ▾] [gpt-4 ▾]                    [⏹️] [⏳]     │
│                                      ^^^^  ^^^      │
│                                      STOP  Loading  │
└─────────────────────────────────────────────────────┘
```

## After Stopping (Cancelled State)

```
┌─────────────────────────────────────────────────────┐
│ AI Chat Panel                                       │
├─────────────────────────────────────────────────────┤
│                                                     │
│ User: Create a sorting algorithm                   │
│                                                     │
│ AI: [Thinking...]                                  │
│ ⚙️ Reading code ✓ Success                          │
│ ⚙️ Analyzing step variables ✓ Success             │
│                                                     │
│ System: Conversation stopped by user.              │
│                                                     │
├─────────────────────────────────────────────────────┤
│ ┌─────────────────────────────────────────────┐   │
│ │ Type your message here...                   │   │
│ │                                             │   │
│ └─────────────────────────────────────────────┘   │
│                                                     │
│ [Ask ▾] [gpt-4 ▾]                         [►]     │
│                                                     │
└─────────────────────────────────────────────────────┘
```

## UI Element Details

### Stop Button

- **Icon**: ⏹️ (stop icon)
- **Color**: Red (danger style)
- **Size**: Small, circular (28x28px)
- **Position**: Left of the send button, right side of input controls
- **Visibility**: Only visible when `isGeneratingCode === true`
- **Tooltip**: "Stop generation"

### Send Button

- **Icon**: ► (play/send icon) or ⏳ (loading spinner when generating)
- **Color**: Primary blue
- **Size**: Small, circular (28x28px)
- **Position**: Right side of input controls
- **State**: Disabled when generating or when input is empty

### Input Field

- **State**: Disabled (grayed out) when `isGeneratingCode === true`
- **Placeholder**: "Ask AI to help with your code..."
- **Rows**: 3 (multi-line textarea)

## User Flow

1. User types a message in the input field
2. User clicks the send button (►)
3. Stop button (⏹️) appears, send button shows loading (⏳)
4. Input field becomes disabled
5. AI starts generating response with streaming updates
6. **User clicks stop button** ← NEW FEATURE
7. Generation is cancelled immediately
8. System message appears: "Conversation stopped by user."
9. UI returns to ready state (stop button disappears, input enabled)
10. User can continue with a new message

## Technical Notes

- The stop button only appears during active generation
- Clicking stop cancels the underlying `CancellationToken`
- Both the UI and backend properly handle the cancellation
- Any streaming messages in progress are cleaned up
- The conversation is properly ended on the server side
