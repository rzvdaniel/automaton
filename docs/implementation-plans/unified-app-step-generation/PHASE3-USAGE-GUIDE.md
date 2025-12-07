# Phase 3: Unified AI System Prompt - Usage Guide

## Overview

Phase 3 introduces a unified AI system prompt that enables generation of both frontend (HTML/CSS/JavaScript) and backend (Custom Steps) code in a single interaction. This dramatically simplifies the app development workflow by allowing users to describe complete applications in natural language.

## What's New

### Before Phase 3 (Complex, Multi-Step):
1. Go to Apps page → Create app → Use AI to generate frontend
2. Navigate to Custom Steps page → Create step → Use AI to generate backend
3. Navigate to Flow Designer → Create flow → Add the custom step
4. Go back to Apps page → Manually add `executeFlow()` calls to frontend

### After Phase 3 (Simple, Single-Step):
1. Go to Apps page → Create app → Describe complete app with backend needs
2. AI automatically:
   - Creates Custom Steps for backend logic
   - Generates frontend HTML/CSS/JavaScript
   - Wires frontend to backend with proper `executeStep()` calls
   - Links steps to the app

## How It Works

### The AI Analyzes Your Request

When you describe your app, the AI analyzes whether backend logic is needed:

**Backend IS NEEDED for:**
- API calls to external services (SendGrid, Stripe, databases)
- Server-side data processing or validation
- Sensitive operations (API keys, credentials)
- Complex computations that should run on server
- File operations or system commands

**Backend IS NOT NEEDED for:**
- Pure UI/display components
- Client-side only interactions
- Static content pages
- Simple form validation (client-side)

### Automatic Backend Step Creation

When backend is needed, the AI automatically:
1. Creates Custom Steps with appropriate names (e.g., `SendEmailStep`)
2. Defines input/output variables with proper types
3. Generates Python/Batch code for the step
4. Links the step to your app
5. Generates frontend code that calls the step

## Example Use Cases

### Example 1: Contact Form with Email

**User Request:**
```
Create a contact form that sends emails via SendGrid
```

**What the AI Creates:**

1. **Backend Step: `SendEmailStep` (Python)**
   - Input variables: `api_key` (sensitive), `to_email`, `subject`, `message`
   - Output variables: `success`, `message_id`, `error_message`
   - Python code that calls SendGrid API

2. **Frontend: Contact Form**
   - HTML form with email, subject, message fields
   - JavaScript that calls `automatonApi.executeStep('SendEmailStep', {...})`
   - Proper loading states and error handling
   - Success/error message display

**Usage:**
1. Set the `api_key` input variable in the step's settings (Backend Actions tab)
2. Preview the app to test the form

### Example 2: Weather App

**User Request:**
```
Create a weather app that shows current weather for any city
```

**What the AI Creates:**

1. **Backend Step: `FetchWeatherStep` (Python)**
   - Input variables: `city`, `api_key` (sensitive)
   - Output variables: `success`, `weather_data`, `error_message`
   - Python code that calls weather API

2. **Frontend: Weather Display**
   - City input field
   - Search button
   - Weather display with temperature, conditions, icon
   - JavaScript that calls `automatonApi.executeStep('FetchWeatherStep', {...})`

### Example 3: Pure Frontend (No Backend)

**User Request:**
```
Create a beautiful landing page for a coffee shop
```

**What the AI Creates:**

- Only frontend HTML/CSS/JavaScript
- No backend steps created (not needed for static content)
- Responsive design with modern styling

## Using the Unified AI System Prompt

### In the App Designer

1. **Open or create an app** in the Apps page
2. **Click the AI Chat panel** (right side)
3. **Describe your complete app** including both frontend and backend needs
4. **The AI will automatically:**
   - Analyze your request
   - Create backend steps if needed
   - Generate frontend code
   - Link everything together

### Example Prompts

**Good prompts that will create backend steps:**
- "Create a form that sends emails using SendGrid"
- "Build a page that fetches data from a REST API"
- "Make an app that processes CSV files and shows statistics"
- "Create a payment form using Stripe"

**Prompts that will create frontend only:**
- "Create a beautiful landing page"
- "Build a portfolio page with image gallery"
- "Make a static pricing page"

## Backend Actions Tab

After the AI creates backend steps, you can manage them in the **Backend Actions** tab:

- **View all linked steps** - See which steps are connected to your app
- **Link/unlink steps** - Check/uncheck boxes to manage connections
- **Edit steps** - Click the edit icon to modify step code
- **Test steps** - Use the step designer to test execution

## Best Practices

### Describing Your App

1. **Be specific about backend needs:**
   - ✅ "Create a contact form that sends emails via SendGrid"
   - ❌ "Create a contact form" (AI might not know backend is needed)

2. **Mention the service/technology:**
   - ✅ "Process payments using Stripe API"
   - ❌ "Process payments" (AI needs to know what API/service)

3. **Describe the complete user flow:**
   - ✅ "User enters email and message, clicks send, sees success confirmation"
   - ❌ "Email form" (too vague)

### Security

1. **Never hardcode API keys** in frontend code
2. **Use input variables** for sensitive data (API keys, passwords)
3. **Set variable values** in the step settings (Backend Actions tab)
4. **Mark sensitive inputs** as "IsSensitive" in variable definitions

### Error Handling

The unified prompt ensures proper error handling:

**Backend (Python):**
```python
success = False
error_message = ""
try:
    # Your logic
    success = True
except Exception as e:
    error_message = str(e)
```

**Frontend (JavaScript):**
```javascript
try {
    const result = await automatonApi.executeStep('StepName', {...});
    if (result.success && result.outputVariables?.success) {
        // Handle success
    } else {
        // Handle backend error
    }
} catch (error) {
    // Handle network error
} finally {
    // Reset UI state
}
```

## Modifying Existing Apps

If you have an existing app and want to add backend functionality:

1. **Open the app** in the designer
2. **In AI Chat, say:** "Add a backend step that [describes functionality]"
3. **The AI will:**
   - Create the new step
   - Update your frontend to use it
   - Link the step to the app

## Troubleshooting

### AI didn't create a backend step when I needed one

**Solution:** Be more explicit about the backend requirement:
- Instead of: "Create a contact form"
- Try: "Create a contact form that sends emails using SendGrid API"

### Step execution fails with "missing input"

**Solution:** Set the input variable values in the step settings:
1. Go to Backend Actions tab
2. Find your step
3. Click edit icon
4. Set the input variable values (especially API keys)

### Frontend doesn't show step results

**Solution:** Check that your frontend properly handles the step response:
- Verify the step returns expected output variables
- Check browser console for JavaScript errors
- Ensure you're accessing `result.outputVariables` correctly

## Technical Details

### System Prompt Structure

The unified system prompt includes:
- Capability description (frontend + backend)
- Analysis rules (when backend is needed)
- Workflow guidelines (create backend before frontend)
- Naming conventions (PascalCase for steps, snake_case for variables)
- Code patterns and best practices
- Error handling templates
- Security guidelines

### Available MCP Tools

The AI has access to these tools:

**Frontend:**
- `get_current_code` - Read existing HTML/CSS/JavaScript
- `save_app_code` - Save frontend code

**Backend:**
- `create_backend_step` - Create a new Custom Step
- `save_step_code` - Save code to a step
- `suggest_step_variables` - Define input/output variables
- `link_step_to_app` - Link step to current app

**Context:**
- `get_linked_steps` - List steps already linked to this app

## What's Next: Phase 4

Phase 4 will add enhanced MCP tools for:
- Auto-variable suggestion based on code analysis
- Step testing and execution from AI context
- Advanced debugging helpers
- Flow generation for complex multi-step workflows (Phase 6)

## Feedback

The unified system prompt is designed to make full-stack app creation effortless. As you use it:
- Note which types of requests work well
- Note which requests need clarification
- Share examples of great AI-generated apps
- Report any issues or unexpected behavior
