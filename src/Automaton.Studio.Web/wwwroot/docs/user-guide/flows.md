# Working with Flows

Flows are the heart of Automaton - they represent your automation workflows as visual, connected diagrams that are easy to understand and modify.

## Understanding Flows

A **Flow** is a sequence of connected steps that perform automation tasks. Think of it as a flowchart where each box represents an action, and the lines show the order of execution.

### Key Components

- **Start Node**: Where your flow begins execution
- **Steps**: Individual actions like HTTP requests, file operations, or custom scripts
- **Connections**: Lines that define the execution path
- **Variables**: Data that passes between steps

## Creating a New Flow

1. **Navigate to Flows**: Click **Flows** in the left sidebar
2. **Create Flow**: Click the **+ Create Flow** button
3. **Name Your Flow**: Give it a descriptive name
4. **Choose Template**: Start with a blank flow or use a sample

## The Flow Designer

### Canvas
The main area where you design your flow by dragging and connecting steps.

### Toolbox
Located on the left side, contains all available steps you can add to your flow:
- **Basic Steps**: Display, Wait, Variable operations
- **HTTP Steps**: GET, POST, PUT requests
- **Logic Steps**: Conditions, loops, switch statements
- **File Operations**: Read, write, copy files
- **Custom Steps**: Your own Python scripts and extensions

### Properties Panel
When you select a step, the right panel shows its configuration options:
- **Input Parameters**: Data the step needs to run
- **Output Variables**: Data the step produces for other steps
- **Error Handling**: How to handle failures

## Building Your Flow

### Adding Steps
1. **Drag from Toolbox**: Drag a step from the toolbox to the canvas
2. **Connect Steps**: Click and drag from one step's output to another's input
3. **Configure Step**: Click on a step to set its properties

### Connecting Steps
- **Green connectors**: Normal execution path
- **Red connectors**: Error handling path
- **Blue connectors**: Conditional paths (true/false)

### Using Variables
Variables let you pass data between steps:

```
Step 1 (HTTP GET) -> {response_data} -> Step 2 (Process Data)
```

- **Create Variables**: Set output variable names in step properties
- **Use Variables**: Reference them in other steps as `{variable_name}`
- **Variable Types**: Text, numbers, JSON objects, files

## Flow Management

### Organizing Flows
- **Search**: Find flows quickly by name or content
- **Favorites**: Mark important flows for quick access
- **Tags**: Label flows by purpose or department

### Scheduling Flows
Set up your flows to run automatically:
- **Once**: Run at a specific date/time
- **Recurring**: Daily, weekly, monthly patterns
- **Event-Based**: Trigger on external events

## Best Practices

- **Start Simple**: Begin with basic steps and add complexity gradually
- **Clear Naming**: Use descriptive names for flows and variables
- **Error Handling**: Always plan for what happens when things go wrong
- **Regular Testing**: Run flows periodically to ensure they still work

## Next Steps

- Learn about [Runners](runners.md) to execute your flows
- Explore [Custom Steps](custom-steps.md) to extend functionality
- Check the [API Reference](../api/flows.md) for advanced integration