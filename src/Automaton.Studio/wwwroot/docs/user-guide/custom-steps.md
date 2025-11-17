# Custom Steps

Custom Steps allow you to extend Automaton's functionality by creating your own reusable automation components using Python scripts or .NET code.

## What are Custom Steps?

Custom Steps are user-defined actions that can be added to your flows, just like built-in steps. They allow you to:
- **Integrate with specific APIs** not covered by built-in steps
- **Perform complex data processing** with custom logic
- **Reuse common operations** across multiple flows
- **Add organization-specific functionality** to your automation platform

## Types of Custom Steps

### Python Steps
Write custom logic using Python:
- **Scripting**: Full Python script execution
- **API Integration**: Connect to external services
- **Data Processing**: Transform and manipulate data
- **File Operations**: Advanced file handling

### .NET Steps
For high-performance scenarios:
- **Compiled Logic**: Pre-compiled .NET assemblies
- **System Integration**: Deep Windows/Linux integration
- **Database Access**: Direct database operations
- **Performance Critical**: CPU-intensive operations

## Creating Your First Custom Step

### Using the Step Designer

1. **Navigate to Custom Steps**: Click **Steps** in the left sidebar
2. **Create New Step**: Click **+ Create Custom Step**
3. **Choose Type**: Select Python or .NET step
4. **Configure Metadata**:
   - **Name**: Descriptive step name
   - **Category**: Organize steps by function
   - **Description**: Help users understand the step's purpose
   - **Icon**: Visual representation in the toolbox

### Python Step Example

Let's create a simple "Generate UUID" step:

```python
import uuid
import json

# Step inputs (from step configuration)
input_format = step_input.get('format', 'standard')  # 'standard' or 'short'

# Generate UUID
if input_format == 'short':
    generated_uuid = str(uuid.uuid4()).replace('-', '')[:8]
else:
    generated_uuid = str(uuid.uuid4())

# Step outputs (available to other steps)
step_output = {
    'uuid': generated_uuid,
    'timestamp': step_context.execution_time.isoformat()
}

# Logging
step_logger.info(f"Generated UUID: {generated_uuid}")
```

### Step Configuration

Define inputs and outputs for your step:

#### Input Parameters
```json
{
  "inputs": [
    {
      "name": "format",
      "type": "string",
      "required": false,
      "default": "standard",
      "description": "UUID format: 'standard' or 'short'",
      "options": ["standard", "short"]
    }
  ]
}
```

#### Output Variables
```json
{
  "outputs": [
    {
      "name": "uuid",
      "type": "string",
      "description": "Generated UUID"
    },
    {
      "name": "timestamp",
      "type": "string",
      "description": "Generation timestamp"
    }
  ]
}
```

## Advanced Python Steps

### API Integration Example

```python
import requests
import json

# Get step inputs
api_url = step_input.get('url')
headers = step_input.get('headers', {})
payload = step_input.get('payload', {})

try:
    # Make API call
    response = requests.post(api_url, json=payload, headers=headers, timeout=30)
    response.raise_for_status()
    
    # Process response
    result = response.json()
    
    # Set outputs
    step_output = {
        'status_code': response.status_code,
        'response_data': result,
        'success': True
    }
    
    step_logger.info(f"API call successful: {response.status_code}")
    
except requests.exceptions.RequestException as e:
    step_output = {
        'error': str(e),
        'success': False
    }
    step_logger.error(f"API call failed: {e}")
    # You can choose to raise an exception to fail the flow
    # raise Exception(f"API call failed: {e}")
```

### Data Processing Example

```python
import pandas as pd
import json

# Get input data (could be from previous steps)
data_source = step_input.get('data_source')  # 'file' or 'variable'
file_path = step_input.get('file_path')
data_variable = step_input.get('data_variable')

try:
    # Load data
    if data_source == 'file':
        df = pd.read_csv(file_path)
    else:
        # Assume JSON data from previous step
        data = json.loads(data_variable)
        df = pd.DataFrame(data)
    
    # Process data
    processed_df = df.groupby('category').sum()
    
    # Convert to output format
    result = processed_df.to_dict('records')
    
    step_output = {
        'processed_data': result,
        'record_count': len(result)
    }
    
    step_logger.info(f"Processed {len(df)} records into {len(result)} groups")
    
except Exception as e:
    step_logger.error(f"Data processing failed: {e}")
    raise
```

## Available Contexts and Utilities

### Step Context
Information about the current execution:
```python
# Execution information
step_context.flow_id          # Current flow ID
step_context.execution_id     # Unique execution ID
step_context.execution_time   # When step started
step_context.runner_id        # Which runner is executing
step_context.step_name        # Name of current step

# Flow variables (shared across steps)
flow_variable = step_context.get_flow_variable('variable_name')
step_context.set_flow_variable('variable_name', value)
```

### Logging
```python
# Different log levels
step_logger.debug("Debug information")
step_logger.info("General information")
step_logger.warning("Something might be wrong")
step_logger.error("An error occurred")

# Structured logging
step_logger.info("Processing complete", extra={
    'records_processed': 100,
    'duration_seconds': 5.2
})
```

### File Operations
```python
# Working with files
file_path = step_context.get_temp_file('output.csv')  # Get temp file path
step_context.upload_file(file_path, 'results.csv')   # Make file available to other steps
downloaded_file = step_context.download_file('input.csv')  # Get file from previous step
```

### External Libraries
Common Python libraries are pre-installed:
- `requests` - HTTP client
- `pandas` - Data analysis
- `numpy` - Numerical computing  
- `json` - JSON processing
- `datetime` - Date/time operations
- `os`, `sys` - System operations

## .NET Custom Steps

For advanced scenarios, you can create compiled .NET steps:

### Step Class Structure

```csharp
using Automaton.Core.Models;
using Automaton.Core.Interfaces;

public class MyCustomStep : IAutomatonStep
{
    public string Name => "My Custom Step";
    public string Category => "Custom";
    public string Description => "Does custom processing";
    
    public async Task<StepResult> ExecuteAsync(StepContext context)
    {
        try
        {
            // Get inputs
            var inputValue = context.GetInput<string>("input_parameter");
            
            // Perform processing
            var result = ProcessData(inputValue);
            
            // Set outputs
            context.SetOutput("result", result);
            
            // Log information
            context.Logger.Information("Step completed successfully");
            
            return StepResult.Success();
        }
        catch (Exception ex)
        {
            context.Logger.Error(ex, "Step execution failed");
            return StepResult.Failure(ex.Message);
        }
    }
    
    private string ProcessData(string input)
    {
        // Your custom logic here
        return input.ToUpper();
    }
}
```

## Managing Custom Steps

### Version Control
- **Source Control**: Store step code in your repository
- **Versioning**: Track changes and manage deployments
- **Testing**: Create unit tests for your custom steps

### Distribution
- **Export Steps**: Package steps for sharing
- **Import Steps**: Add steps from other team members
- **Step Library**: Build a collection of reusable steps

### Monitoring
- **Usage Analytics**: See which custom steps are most used
- **Performance Metrics**: Monitor execution time and resource usage
- **Error Tracking**: Identify problematic steps

## Best Practices

### Development Guidelines
- **Error Handling**: Always handle exceptions gracefully
- **Logging**: Provide detailed logging for troubleshooting
- **Validation**: Validate inputs before processing
- **Documentation**: Include clear descriptions and examples

### Performance Considerations
- **Timeouts**: Set reasonable timeouts for external calls
- **Resource Usage**: Be mindful of memory and CPU consumption
- **Async Operations**: Use async/await for I/O operations
- **Connection Pooling**: Reuse connections where possible

### Security
- **Input Sanitization**: Validate and sanitize all inputs
- **Credentials**: Use secure credential storage
- **Network Access**: Minimize external network calls
- **Sensitive Data**: Don't log sensitive information

## Testing Custom Steps

### Unit Testing
```python
# test_custom_step.py
import unittest
from unittest.mock import Mock

class TestGenerateUUIDStep(unittest.TestCase):
    def setUp(self):
        self.mock_context = Mock()
        self.mock_logger = Mock()
        
    def test_generate_standard_uuid(self):
        # Test standard UUID generation
        step_input = {'format': 'standard'}
        # Execute step and verify output
        
    def test_generate_short_uuid(self):
        # Test short UUID generation
        step_input = {'format': 'short'}
        # Execute step and verify output
```

### Integration Testing
- **Flow Testing**: Create test flows using your custom steps
- **Runner Testing**: Test on actual runners
- **End-to-End**: Test complete automation scenarios

## Troubleshooting

### Common Issues
- **Import Errors**: Check library availability and versions
- **Timeout Issues**: Adjust timeout settings for long-running operations
- **Memory Errors**: Optimize data handling for large datasets
- **Permission Errors**: Verify file and network permissions

### Debugging
- **Local Testing**: Test steps outside of flows first
- **Verbose Logging**: Add detailed logging for troubleshooting
- **Step-by-Step Execution**: Use flow debugger to isolate issues
- **Variable Inspection**: Check input/output variable values

## Advanced Topics

### Dynamic Step Creation
- **Runtime Generation**: Create steps based on configuration
- **Template Steps**: Use templates for similar operations
- **Conditional Logic**: Steps that adapt based on inputs

### Integration Patterns
- **Database Access**: Connect to various database systems
- **Message Queues**: Integrate with RabbitMQ, Azure Service Bus
- **Cloud Services**: AWS, Azure, Google Cloud integrations
- **Enterprise Systems**: SAP, Salesforce, SharePoint

### Performance Optimization
- **Caching**: Cache frequently accessed data
- **Parallel Processing**: Execute operations concurrently
- **Batch Operations**: Process multiple items together
- **Resource Pooling**: Reuse expensive resources

## Next Steps

- Explore the [Step Designer](../developer-guide/step-designer.md) for advanced features
- Learn about [Flow Integration](flows.md) to use your custom steps
- Check the [API Reference](../api/steps.md) for programmatic step management
- Review [Sample Steps](../reference/sample-steps.md) for inspiration