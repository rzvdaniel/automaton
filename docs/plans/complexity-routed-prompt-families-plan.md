## Plan: Complexity-Routed Prompt Families for Apps + Steps

Adopt a complexity-routed prompt architecture with 4 tiers (Simple, Medium, Large, Enterprise) selected by an LLM classifier pass. This provides immediate scale improvements for HTML/CSS/JS apps and backend steps by introducing feature-based folder guidance, advanced architecture patterns, and backend "controller-style" step sets based on the detected complexity.

### Phased Implementation Plan

**Phase 1: Core Routing & Classifier Foundation**
1. **Define Routing Contract**: Create models in the server AI conversation flow (`PromptFamily`, `ComplexityTier`, `Confidence`, `Rationale`) and thread them through prompt build calls.
2. **Implement Prompt Classifier Service**: Add a dedicated LLM-only classifier service invoked from `AiConversationController` before prompt generation for `WebApp` and `CustomStep`. Include fallback behavior for low confidence.
3. **State Persistence**: Add conversation-level prompt selection metadata persistence (session/message metadata) so the selected tier is auditable and reused in continuation chats to prevent prompt drift.

**Phase 2: Frontend Complexity Tiers (App Prompts)**
1. **Create Tiered Templates**: Introduce 4 App prompt templates as embedded resources (`app-simple.md`, `app-medium.md`, `app-large.md`, `app-enterprise.md`).
2. **Scale Architecture Guidance**:
   - *Simple/Medium*: Flat file structure (`index.html`, `styles/main.css`, `js/app.js`).
   - *Large/Enterprise*: Feature-based folder organization (e.g., `features/auth/`, `features/dashboard/`), strict separation of concerns, and modular vanilla JS patterns.
3. **Update Builder**: Modify `AppSystemPromptBuilder` to select the appropriate template based on the classifier's output.

**Phase 3: Dedicated App Backend Controllers (New Entity)**
1. **Introduce `AppController` Entity**: Create a new backend entity specifically for Web Apps, separate from `CustomStep`. While Steps remain single-purpose building blocks for visual flows, `AppControllers` will handle complex, multi-action routing (e.g., a `UsersController` with `create`, `read`, `update`, `delete` methods). See [app-controller-execution-architecture.md](app-controller-execution-architecture.md) for detailed execution model.
2. **File System Storage**: Store controller code in the app's `backend/` folder (e.g., `backend/users-controller.py`, `backend/products-controller.py`) alongside UI files, managed by `AppFileStorageService`.
3. **Execution Model**: 
   - Server loads controller code from the file system and sends it to runners via SignalR (Option A from architecture doc).
   - Runner executes the action method and returns results.
   - Supports `automatonApi.executeController('Users', 'create', payload)` and similar calls.
4. **Create Tiered Prompts**:
   - Introduce AI prompt templates for AppControllers (`controller-simple.md`, `controller-large.md`, etc.).
5. **Scale Backend Architecture**:
   - *Simple/Medium*: Continue using single-purpose `CustomStep`s for basic integrations.
   - *Large/Enterprise*: Instruct the AI to generate `AppController`s to manage complex state and group related operations, keeping the Step library clean.

**Phase 4: Tool Policy & Validation**
1. **Align Tool Policies**: Update prompts to enforce tier-specific tool usage (e.g., Large/Enterprise tiers *must* use `list_files` and targeted `read_file` before making edits, and must explicitly test controller steps with multiple action inputs).
2. **End-to-End Validation**: Test with representative scenarios:
   - *Simple*: Landing page.
   - *Medium*: Dashboard with 1-2 API calls.
   - *Large*: Online store with feature folders and a `ProductControllerStep`.
   - *Enterprise*: CRM module with complex state and multiple controller steps.

### Relevant Files
- `src/Automaton.Studio.Server/Controllers/AiConversationController.cs` — Orchestrate classifier call and prompt routing in `BuildSystemPromptAsync`.
- `src/Automaton.Mcp/SystemPrompts/AppSystemPromptBuilder.cs` — Add prompt family/tier selection API and embedded resource loading strategy.
- `src/Automaton.Mcp/SystemPrompts/StepSystemPromptBuilder.cs` — Add tier-based templates and retain language augmentation.
- `src/Automaton.Mcp/SystemPrompts/AppSystemPrompt.md` — Split into tiered family files and keep common shared rules in reusable sections.
- `src/Automaton.Mcp/Automaton.Mcp.csproj` — Register new embedded prompt resources.
- `src/automaton-studio-vue/src/shared/composables/useAiChat.ts` — Verify/align request payload fields for routing metadata if needed.
- `src/Automaton.Studio.Server/Services/Mcp/AppMcpToolProvider.cs` — Ensure prompt/tool conventions map to available tools and legacy compatibility.

### Verification
1. **Unit test classifier service**: Deterministic parsing of classifier output JSON, low-confidence fallback, and error fallback.
2. **Unit test prompt builders**: Each tier returns expected template + language/tier appendices.
3. **Integration test conversation endpoint**: Given app/step requests of different complexity, assert selected prompt family metadata and non-empty system prompt.
4. **Integration test continuation sessions**: Existing session retains or intentionally re-evaluates tier according to chosen policy.
5. **Manual MCP validation**: Confirm generated app file structure (feature folders) and backend step orchestration (controller-style) match tier intent.

### Decisions
- **Confirmed**: Complexity detection uses LLM classifier only.
- **Confirmed**: Start with 4 tiers (Simple, Medium, Large, Enterprise).
- **Confirmed**: Frontend remains HTML/CSS/JS, scaled via feature-based folder structures for large apps.
- **Confirmed**: Backend scales via a new `AppController` entity for complex apps, preserving `CustomStep` as a pure, single-purpose building block for flows.

### Further Considerations
1. **Tier Stickiness Policy**: Once a session starts at Large/Enterprise, prefer sticky tier for continuity unless user explicitly requests simplification.
2. **Classifier Cost/Latency**: Consider caching classification per session/resource to minimize extra model calls.
3. **Context Window Limits**: For Enterprise tier, enforce strict file-reading limits in the prompt to prevent the AI from overwhelming its context window when reading large feature directories.
