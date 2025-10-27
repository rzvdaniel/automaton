# Technical Architecture: Step Input/Output Variable Handling

**Document Version:** 1.0  
**Date:** October 2024  
**Status:** Proposal for Refactoring  
**Scope:** Automaton.Steps.CustomStep & Automaton.Core.Models.WorkflowStep  

---

## Executive Summary

The current implementation has a critical architectural gap: **type information flows in only one direction**. Input variables are pre-defined with types but are blindly converted to strings before execution, while output variables cannot safely handle complex types returned from Python execution. This document outlines the problem, its implications, and a robust solution.

---

## 1. Current Architecture Analysis

### 1.1 Current Flow

```
???????????????????????????????????????????????????????????????????
?           CUSTOM STEP EXECUTION    ?
???????????????????????????????????????????????????????????????????

INPUT PHASE:
  ????????????????????????????????????????????????????????
  ? 1. WorkflowStep.Inputs (IDictionary<string, StepVariable>)
  ?    - Each StepVariable has: Name, Type, Value        ?
  ?    - Type: String, Text, Boolean, Number, Date     ?
  ? 2. CustomStep.CodeInputVariables (IList<StepVariable>)
  ?    - Subset of inputs passed to script        ?
  ? 3. Conversion:      ?
  ?    CodeInputVariables.ToDictionary()   ?
  ?    ?? Key: variable.Name    ?
  ?    ?? Value: variable.Value (as object)       ?
  ????????????????????????????????????????????????????????
  ?
  ????????????????????????????????????????????????????????
  ? 4. ScriptEngineHost.Execute()         ?
  ?    - Input: IDictionary<string, object>        ?
  ?    - Output: IDictionary<string, dynamic>       ?
  ?      ?
  ?    PROBLEM #1: Type information LOST    ?
  ?    - All inputs are object/dynamic        ?
  ?    - Python receives strings, objects, or nulls     ?
  ?    - No type validation before execution ?
  ????????????????????????????????????????????????????????
   ?
  ????????????????????????????????????????????????????????
  ? 5. PythonNetScriptEngine.Execute()           ?
  ?    - ConvertToPython(object value)   ?
  ?    - Converts C# objects to Python objects      ?
  ?    - Example: List ? PyList, Dict ? PyDict?
  ?    ?
  ?    ISSUE: No validation that input types match      ?
  ?    step's declared input requirements               ?
  ????????????????????????????????????????????????????????
            ?
  ????????????????????????????????????????????????????????
  ? 6. Python Execution      ?
  ?    - Script expects certain types, receives others  ?
  ?    - Runtime errors possible if types mismatch      ?
  ?  - Example: script expects List, receives string  ?
  ????????????????????????????????????????????????????????

OUTPUT PHASE:
  ????????????????????????????????????????????????????????
  ? 7. Python returns: IDictionary<string, dynamic>    ?
  ?    - Example: { "headlines": [list of dicts], ... }?
  ?    - Types are dynamic/unknown at this point?
  ? ?
  ?    PROBLEM #2: Type information NOT AVAILABLE      ?
  ?    - Step declared output.Type = "String"          ?
  ?    - Python returned a List        ?
  ?    - No conversion/validation occurs             ?
  ????????????????????????????????????????????????????????
          ?
  ????????????????????????????????????????????????????????
  ? 8. CustomStep.RunAsync() Output Handling:    ?
  ?              ?
  ? foreach (var variable in Outputs)           ?
  ?    {    ?
  ?        if (scriptVariables.ContainsKey(variable...))?
  ?        {            ?
  ?            variable.Value.Value =    ?
  ?   scriptVariables[...].ToString();      ?
  ?        }  ?
  ?    }             ?
  ?         ?
  ?    CRITICAL ISSUE:  ?
  ?  - .ToString() on complex types loses structure   ?
  ?    - List ? "System.Collections.Generic.List`1"   ?
  ?    - Dictionary ? serialized JSON string (maybe)   ?
  ?    - Type casting ignored: Type != actual result   ?
  ????????????????????????????????????????????????????????
```

### 1.2 Problems Identified

#### Problem 1: Input Type Mismatch (Unvalidated)
- **Symptom:** No validation that input values match declared types before execution
- **Example:** 
  - Step declares: `Inputs["api_key"] = StepVariable { Type = String }`
  - But receives: Boolean or Number
  - Python script receives unexpected type ? runtime error
- **Root Cause:** Type information from `StepVariable.Type` is never checked
- **Impact:** Silent failures or cryptic Python errors

#### Problem 2: Output Type Loss
- **Symptom:** Complex return types (List, Dict) are lost when converted to strings
- **Example:**
  - Step declares: `Outputs["headlines"] = StepVariable { Type = ??? }`
  - Python returns: `[{"title": "...", "url": "..."}, ...]`
  - Result stored as: `"[{'title': '...', 'url': '...'}]"` (string representation)
  - Type mismatch on reassignment
- **Root Cause:** `ToString()` used for all output types
- **Impact:** Data structure information lost; UI cannot properly display/edit

#### Problem 3: No Serialization Strategy
- **Issue:** How do complex types (List, Dict) get serialized for storage and retrieval?
- **Current:** Each type manually handles ToString(); no consistent strategy
- **Result:** Inconsistent behavior across steps

#### Problem 4: Type Information Unused
- **Issue:** `StepVariable.Type` enum exists but is not used in conversions
- **Current:** `VariableType` values are decorative; never validated or enforced
- **Result:** Type safety is theoretical, not practical

---

## 2. Example: NYTimes Headlines Step

### Current Problematic Behavior

```csharp
// Define outputs BEFORE execution
public IDictionary<string, StepVariable> Outputs = new()
{
    ["headlines"] = new StepVariable 
    { 
    Name = "headlines",
        Type = VariableType.??? // What goes here? Text? String?
        Value = null 
    }
};

// In CustomStep.RunAsync():
var scriptVariables = scriptHost.Execute(resource, inputVariables);

// Output assignment
foreach (var variable in Outputs)
{
    if (scriptVariables.ContainsKey("headlines"))
    {
 // THIS IS THE PROBLEM:
        // Python returned: List<Dictionary<string, string>>
        // But we store it as: String representation
        variable.Value.Value = scriptVariables["headlines"].ToString();
        // Result: "[{'title': '...', 'url': '...'}]"
        // Original structure lost!
    }
}
```

**What we need:**
```csharp
Outputs = new()
{
    ["headlines"] = new StepVariable 
    { 
  Name = "headlines",
        Type = VariableType.List,  // ? New type needed
        ItemType = VariableType.Dictionary,  // ? New property
        DictionaryKeyType = VariableType.String,
   DictionaryValueType = VariableType.String,
        Value = null 
    }
};
```

---

## 3. Proposed Solution

### 3.1 Extend VariableType Enum

```csharp
namespace Automaton.Core.Enums;

/// <summary>
/// Represents the data type of a workflow step variable.
/// Supports scalar types, collections, and complex objects.
/// </summary>
public enum VariableType
{
    // Scalar types
String,
    Text,
    Boolean,
    Number,
    Integer,
    Decimal,
    Date,
    DateTime,
    TimeSpan,
    Guid,
    Null,
    
    // Collection types
    List,
    Dictionary,
    Array,
    Set,
    
    // Complex types
    Object,  // For untyped Python objects/JSON
    Json,    // For explicit JSON serialization
    
    // File types
    File,
    Stream
}
```

### 3.2 Enhanced StepVariable Class

```csharp
namespace Automaton.Core.Models;

/// <summary>
/// Enhanced variable descriptor with complete type information.
/// Supports scalar types, collections, and nested complex types.
/// </summary>
public class StepVariable
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    
    // PRIMARY TYPE
    public VariableType Type { get; set; } = VariableType.String;
    
    // TYPE INFORMATION FOR COLLECTIONS
    public VariableType? ItemType { get; set; }  // For List, Set, Array
    
    // TYPE INFORMATION FOR DICTIONARIES
    public VariableType? DictionaryKeyType { get; set; }  // Usually String
    public VariableType? DictionaryValueType { get; set; }  // Can be any type
    
    // RUNTIME VALUE
    public virtual object? Value { get; set; }
    
    // METADATA
  public bool IsRequired { get; set; }
    public object? DefaultValue { get; set; }
    public List<string>? AllowedValues { get; set; }  // For validation
    public string? ValidationRegex { get; set; }  // For string validation
  
    // HELPER METHODS
    
    /// <summary>
    /// Get the expected .NET type for this variable.
    /// Used for type validation and conversion.
    /// </summary>
    public Type GetExpectedDotNetType()
    {
     return Type switch
        {
VariableType.String => typeof(string),
      VariableType.Text => typeof(string),
VariableType.Boolean => typeof(bool),
     VariableType.Number => typeof(double),
         VariableType.Integer => typeof(int),
            VariableType.Decimal => typeof(decimal),
       VariableType.Date => typeof(DateTime),
    VariableType.DateTime => typeof(DateTime),
     VariableType.TimeSpan => typeof(TimeSpan),
 VariableType.Guid => typeof(Guid),
    
            VariableType.List => GetListType(),
    VariableType.Dictionary => GetDictionaryType(),
     VariableType.Array => GetArrayType(),
        
     VariableType.Json => typeof(string),  // Stored as JSON string
            VariableType.Object => typeof(object),
          VariableType.Null => typeof(object),
   
     _ => typeof(object)
        };
    }
    
    private Type GetListType()
    {
        var itemType = ItemType.HasValue 
    ? ResolveType(ItemType.Value)
            : typeof(object);
    return typeof(List<>).MakeGenericType(itemType);
    }
    
    private Type GetDictionaryType()
 {
        var keyType = ResolveType(DictionaryKeyType ?? VariableType.String);
        var valueType = ResolveType(DictionaryValueType ?? VariableType.Object);
        return typeof(Dictionary<,>).MakeGenericType(keyType, valueType);
    }
    
    private Type GetArrayType()
    {
      var itemType = ItemType.HasValue 
       ? ResolveType(ItemType.Value)
      : typeof(object);
        return itemType.MakeArrayType();
    }
    
    private static Type ResolveType(VariableType varType) => varType switch
    {
 VariableType.String => typeof(string),
        VariableType.Boolean => typeof(bool),
        VariableType.Number => typeof(double),
        VariableType.Integer => typeof(int),
        VariableType.Date => typeof(DateTime),
   VariableType.Guid => typeof(Guid),
        VariableType.Object => typeof(object),
        _ => typeof(object)
 };
}
```

### 3.3 New Type Conversion & Validation Service

```csharp
namespace Automaton.Core.Scripting.Conversion;

/// <summary>
/// Handles type conversion and validation for step variables.
/// Ensures inputs match declared types before execution.
/// Converts Python output to declared output types.
/// </summary>
public interface IScriptVariableConverter
{
    /// <summary>
    /// Convert and validate an input value to match the declared type.
    /// </summary>
    /// <param name="value">Raw value from step input</param>
    /// <param name="variable">Step variable with type information</param>
  /// <returns>Converted value ready for Python execution</returns>
    /// <exception cref="VariableTypeException">If conversion fails</exception>
    object? ConvertInputVariable(object? value, StepVariable variable);
    
    /// <summary>
    /// Convert Python output to the declared output variable type.
    /// </summary>
    /// <param name="pythonResult">Value returned from Python</param>
    /// <param name="variable">Step variable with target type information</param>
    /// <returns>Converted value matching declared type</returns>
    /// <exception cref="VariableTypeException">If conversion fails</exception>
    object? ConvertOutputVariable(dynamic pythonResult, StepVariable variable);
    
    /// <summary>
    /// Validate that a value matches the declared type constraints.
    /// </summary>
    /// <param name="value">Value to validate</param>
    /// <param name="variable">Variable with constraints</param>
    /// <returns>True if valid; throws exception if invalid</returns>
    bool ValidateVariable(object? value, StepVariable variable);
}

public class ScriptVariableConverter : IScriptVariableConverter
{
    private readonly ILogger logger;
    
    public ScriptVariableConverter()
    {
        logger = Log.ForContext<ScriptVariableConverter>();
    }
    
    public object? ConvertInputVariable(object? value, StepVariable variable)
    {
        if (value == null)
        {
      if (variable.IsRequired)
    {
   throw new VariableTypeException(
   $"Required input variable '{variable.Name}' is null");
            }
            return variable.DefaultValue ?? null;
      }
        
  try
        {
     return variable.Type switch
    {
        VariableType.String or VariableType.Text => 
 ConvertToString(value),
          VariableType.Boolean => 
  ConvertToBoolean(value),
    VariableType.Integer => 
              ConvertToInteger(value),
  VariableType.Number or VariableType.Decimal => 
          ConvertToDecimal(value),
        VariableType.Date or VariableType.DateTime => 
         ConvertToDateTime(value),
        VariableType.Guid => 
               ConvertToGuid(value),
 VariableType.List => 
          ConvertToList(value, variable),
     VariableType.Dictionary => 
     ConvertToDictionary(value, variable),
       VariableType.Json => 
   JsonConvert.SerializeObject(value),
          VariableType.Object => 
         value,
      _ => value
        };
        }
        catch (Exception ex)
        {
            throw new VariableTypeException(
         $"Failed to convert input variable '{variable.Name}' " +
         $"from type '{value?.GetType().Name}' " +
    $"to '{variable.Type}'", ex);
        }
    }
  
    public object? ConvertOutputVariable(dynamic pythonResult, StepVariable variable)
    {
        if (pythonResult == null)
   {
  return null;
        }
     
        try
        {
            return variable.Type switch
            {
VariableType.String or VariableType.Text => 
          pythonResult.ToString(),
       VariableType.Boolean => 
        Convert.ToBoolean(pythonResult),
     VariableType.Integer => 
   Convert.ToInt32(pythonResult),
          VariableType.Number => 
        Convert.ToDouble(pythonResult),
           VariableType.Decimal => 
         Convert.ToDecimal(pythonResult),
           VariableType.Date or VariableType.DateTime => 
       ConvertPythonToDateTime(pythonResult),
  VariableType.Guid => 
     Guid.Parse(pythonResult.ToString()),
              
        VariableType.List => 
         ConvertPythonToList(pythonResult, variable),
       VariableType.Dictionary => 
  ConvertPythonToDictionary(pythonResult, variable),
         VariableType.Array => 
            ConvertPythonToArray(pythonResult, variable),
          
            VariableType.Json => 
        JsonConvert.SerializeObject(pythonResult),
                VariableType.Object => 
          pythonResult,
     
                _ => pythonResult
            };
 }
      catch (Exception ex)
   {
   throw new VariableTypeException(
    $"Failed to convert output variable '{variable.Name}' " +
                $"from Python type to '{variable.Type}'", ex);
        }
    }
    
    public bool ValidateVariable(object? value, StepVariable variable)
    {
        if (value == null && variable.IsRequired)
        {
  throw new VariableTypeException(
     $"Required variable '{variable.Name}' cannot be null");
   }
  
  if (value == null)
return true;
        
        // Validate string length, regex, allowed values
if (variable.Type == VariableType.String || variable.Type == VariableType.Text)
        {
        var stringValue = value.ToString();
   
        if (!string.IsNullOrEmpty(variable.ValidationRegex))
 {
     var regex = new Regex(variable.ValidationRegex);
         if (!regex.IsMatch(stringValue))
        {
                throw new VariableTypeException(
         $"Variable '{variable.Name}' value does not match " +
      $"required pattern: {variable.ValidationRegex}");
       }
     }

            if (variable.AllowedValues?.Any() == true)
            {
      if (!variable.AllowedValues.Contains(stringValue))
          {
         throw new VariableTypeException(
               $"Variable '{variable.Name}' value '{stringValue}' " +
       $"not in allowed values: {string.Join(", ", variable.AllowedValues)}");
                }
            }
        }
        
        return true;
    }
    
    // Helper conversion methods
    private string ConvertToString(object value)
        => value as string ?? value.ToString() ?? string.Empty;
    
    private bool ConvertToBoolean(object value)
        => value is bool b ? b : 
    bool.TryParse(value?.ToString(), out var result) ? result :
   throw new InvalidCastException($"Cannot convert {value} to boolean");
    
    private int ConvertToInteger(object value)
        => value is int i ? i :
           int.TryParse(value?.ToString(), out var result) ? result :
           throw new InvalidCastException($"Cannot convert {value} to integer");
    
    private decimal ConvertToDecimal(object value)
        => value is decimal d ? d :
           decimal.TryParse(value?.ToString(), out var result) ? result :
           throw new InvalidCastException($"Cannot convert {value} to decimal");
    
    private DateTime ConvertToDateTime(object value)
        => value is DateTime dt ? dt :
         DateTime.TryParse(value?.ToString(), out var result) ? result :
           throw new InvalidCastException($"Cannot convert {value} to datetime");
    
    private Guid ConvertToGuid(object value)
        => value is Guid g ? g :
   Guid.TryParse(value?.ToString(), out var result) ? result :
       throw new InvalidCastException($"Cannot convert {value} to guid");
    
    private List<object> ConvertToList(object value, StepVariable variable)
  {
        // Implementation handles various list formats (arrays, IEnumerable, etc.)
   // ...
    }
    
    private Dictionary<string, object> ConvertToDictionary(object value, StepVariable variable)
    {
        // Implementation handles various dict formats (objects, JSON, etc.)
      // ...
    }
    
    // Python-to-.NET conversion helpers
  private List<object> ConvertPythonToList(dynamic pythonList, StepVariable variable)
  {
        var result = new List<object>();
        // Convert Python list to C# List
    // Respect ItemType from variable for nested type conversion
        // ...
        return result;
    }
    
    private Dictionary<string, object> ConvertPythonToDictionary(dynamic pythonDict, StepVariable variable)
    {
        var result = new Dictionary<string, object>();
        // Convert Python dict to C# Dictionary
 // Respect DictionaryKeyType and DictionaryValueType
        // ...
        return result;
    }
    
    private Array ConvertPythonToArray(dynamic pythonArray, StepVariable variable)
    {
        // Similar to list conversion but returns Array type
        // ...
    }
}

/// <summary>
/// Exception thrown when variable type conversion or validation fails.
/// </summary>
public class VariableTypeException : Exception
{
    public VariableTypeException(string message) : base(message) { }
    public VariableTypeException(string message, Exception inner) 
        : base(message, inner) { }
}
```

### 3.4 Updated CustomStep with Type Conversion

```csharp
namespace Automaton.Steps;

public class CustomStep : WorkflowStep
{
    private readonly ScriptEngineHost scriptHost;
    private readonly IScriptVariableConverter variableConverter;

    [IgnorePropertyParsing(true)]
    public string? Code { get; set; }

    public string Language { get; set; }

    private StepVariableParser StepVariableParser { get; } = new StepVariableParser();

    public IList<StepVariable> CodeInputVariables { get; set; }

    public CustomStep(
        ScriptEngineHost scriptHost,
  IScriptVariableConverter variableConverter)
    {
        this.scriptHost = scriptHost;
        this.variableConverter = variableConverter;
        CodeInputVariables = [];
    }

    protected override Task<ExecutionResult> RunAsync(StepExecutionContext context)
    {
        var result = ExecutionResult.Next();
 var logs = new List<string>();

        void OnScriptTextWrittenLocal(object sender, string text)
        {
        logger.Information("{Text}", text);
            logs.Add(text);
        }

        try
  {
        scriptHost.ScriptTextWritten += OnScriptTextWrittenLocal;

   var resource = new ScriptResource()
            {
       ContentType = GetContentType(),
 Content = Code
            };

      // IMPROVED: Convert and validate inputs BEFORE execution
          var inputVariablesDictionary = ConvertAndValidateInputs();

  var scriptVariables = scriptHost.Execute(resource, inputVariablesDictionary);

            // IMPROVED: Convert and handle outputs with type awareness
      ConvertAndAssignOutputs(scriptVariables, context);

   result.InternalLogs = logs;
       return Task.FromResult(result);
     }
      catch (VariableTypeException ex)
        {
    logger.Error("Variable type error: {Message}", ex.Message);
            result.Success = false;
  result.Error = ex.Message;
       return Task.FromResult(result);
        }
        finally
   {
 scriptHost.ScriptTextWritten -= OnScriptTextWrittenLocal;
    }
    }

    /// <summary>
    /// Convert and validate input variables before passing to Python.
    /// </summary>
    private IDictionary<string, object> ConvertAndValidateInputs()
    {
    var converted = new Dictionary<string, object>();

        foreach (var variable in CodeInputVariables)
     {
            try
            {
   // 1. Validate the variable has required information
        if (string.IsNullOrEmpty(variable.Name))
   {
 throw new VariableTypeException("Variable name cannot be empty");
           }

  // 2. Validate the value matches declared type
          variableConverter.ValidateVariable(variable.Value, variable);

                // 3. Convert to appropriate Python-compatible type
      var convertedValue = variableConverter.ConvertInputVariable(
         variable.Value, 
         variable);

                converted[variable.Name] = convertedValue ?? new object();

     logger.Information(
                    "Input variable '{Name}' converted from {OriginalType} to {TargetType}",
   variable.Name,
  variable.Value?.GetType().Name ?? "null",
  variable.Type);
      }
            catch (VariableTypeException ex)
     {
          logger.Error("Failed to process input variable '{Name}': {Message}",
         variable.Name, ex.Message);
                throw;
      }
        }

        return converted;
    }

 /// <summary>
  /// Convert Python results to declared output types.
    /// </summary>
 private void ConvertAndAssignOutputs(
        IDictionary<string, dynamic> scriptVariables,
   StepExecutionContext context)
    {
  foreach (var outputVariable in Outputs.Values)
        {
  try
            {
        if (!scriptVariables.TryGetValue(outputVariable.Id, out var pythonResult))
     {
        logger.Warning(
        "Output variable '{Name}' not found in script results",
         outputVariable.Name);
                    continue;
                }

    // Convert Python result to declared output type
      var convertedValue = variableConverter.ConvertOutputVariable(
     pythonResult,
          outputVariable);

          outputVariable.Value = convertedValue;

         logger.Information(
   "Output variable '{Name}' converted from Python to {TargetType}",
        outputVariable.Name,
    outputVariable.Type);

   context.Workflow.SetVariable(outputVariable);
            }
    catch (VariableTypeException ex)
            {
                logger.Error(
        "Failed to convert output variable '{Name}': {Message}",
        outputVariable.Name,
     ex.Message);

// Optionally: mark as error or use fallback
          // result.Error = ex.Message;
                throw;
    }
        }
    }

    protected override void SetupProperties(StepExecutionContext context)
  {
        Code = GetInputValue<string>(nameof(Code));
        Language = GetInputValue<string>(nameof(Language));

        var codeInputVariables = GetInputValue<IList<StepVariable>>(nameof(CodeInputVariables));

  foreach (var variable in codeInputVariables)
      {
        var variableValue = StepVariableParser.Parse(variable, context.Workflow);
      variable.Value = variableValue;
  CodeInputVariables.Add(variable);
        }
    }

    private string GetContentType()
    {
        var contentType = Language switch
  {
          CustomStepLanguage.Python => "text/x-python",
     CustomStepLanguage.Batch => "text/x-batch",
            _ => "text/x-python"
        };

     return contentType;
    }
}
```

---

## 4. Implementation Strategy

### Phase 1: Foundation (Week 1-2)
1. ? Extend `VariableType` enum with new types
2. ? Enhance `StepVariable` class with type information
3. ? Create `IScriptVariableConverter` interface and implementation
4. ? Write unit tests for converter

### Phase 2: Integration (Week 3)
1. Update `CustomStep` to use new converter
2. Update `ScriptEngineHost` to support structured types
3. Integration tests with Python examples

### Phase 3: Validation & Testing (Week 4)
1. Test with existing custom steps
2. Fix edge cases
3. Documentation & examples

---

## 5. Benefits of Proposed Solution

| Aspect | Current | Proposed |
|--------|---------|----------|
| **Type Safety** | ? None | ? Full validation |
| **Input Validation** | ? Not done | ? Before execution |
| **Output Serialization** | ? ToString() loses data | ? Structured JSON/dict preservation |
| **Error Messages** | ? Cryptic Python errors | ? Clear type mismatch errors |
| **Complex Types** | ? Not supported | ? List, Dict, nested types |
| **Extensibility** | ? Hard to add types | ? Easy enum + converter |
| **Debugging** | ? Difficult | ? Type conversion logs |

---

## 6. Example: NYTimes Headlines (Refactored)

### Step Definition
```csharp
var headlinesStep = new CustomStep(scriptHost, converter)
{
    Id = "step-nytimes",
    Name = "NYTimes Headlines",
    Code = "... (Python code above) ...",
    Language = "python",
    
    // INPUT DEFINITION
    Inputs = new Dictionary<string, StepVariable>
    {
        ["api_key"] = new StepVariable
        {
   Id = "var-api-key",
 Name = "api_key",
            Type = VariableType.String,
      IsRequired = true,
      ValidationRegex = @"^[a-zA-Z0-9]{32}$"  // Validate API key format
        }
    },
  
    // OUTPUT DEFINITION - NOW TYPE-AWARE
    Outputs = new Dictionary<string, StepVariable>
    {
   ["headlines"] = new StepVariable
        {
   Id = "var-headlines",
     Name = "headlines",
     Type = VariableType.List,
          ItemType = VariableType.Dictionary,
            DictionaryKeyType = VariableType.String,
            DictionaryValueType = VariableType.String,
        // Now UI knows: this is a list of dictionaries
 // Can display as table, format for export, etc.
     }
    }
};
```

### Execution Flow
```csharp
// BEFORE: Input validation skipped, type mismatch possible
// AFTER: Type validation happens here
try 
{
    var validated = converter.ConvertInputVariable(apiKey, inputVar);
    // Throws VariableTypeException if api_key is Boolean or Number
    // Validates regex pattern
// Converts to appropriate Python type
}

// Python executes with validated inputs

// BEFORE: Output is stringified: "[{'title': '...', 'url': '...'}]"
// AFTER: Output structure preserved
var headlines = converter.ConvertOutputVariable(pythonList, outputVar);
// Returns: List<Dictionary<string, object>>
// UI can properly display as table with columns: title, url
```

---

## 7. Migration Path

### Backward Compatibility
```csharp
// Existing code continues to work
public object? ConvertInputVariable(object? value, StepVariable variable)
{
    // If variable.Type is not set (null), treat as String
    var typeToUse = variable.Type ?? VariableType.String;
    // ...
}
```

### Gradual Adoption
1. Implement new converter alongside old code
2. Update CustomStep to use it (other steps can wait)
3. Gradually migrate other step types
4. Remove old code once all steps migrated

---

## 8. Testing Strategy

### Unit Tests
```csharp
[TestClass]
public class ScriptVariableConverterTests
{
    private IScriptVariableConverter converter;
    
    [TestInitialize]
    public void Setup() => converter = new ScriptVariableConverter();
    
    [TestMethod]
    public void ConvertInputVariable_StringToBoolean_ThrowsException()
    {
  var variable = new StepVariable 
 { Name = "test", Type = VariableType.Boolean, IsRequired = true };
        
    Assert.ThrowsException<VariableTypeException>(
            () => converter.ConvertInputVariable("not-a-bool", variable));
    }
    
    [TestMethod]
    public void ConvertOutputVariable_PythonListToList_Succeeds()
    {
        var variable = new StepVariable
 {
            Name = "results",
            Type = VariableType.List,
            ItemType = VariableType.String
        };
        
        dynamic pythonList = new List<object> { "a", "b", "c" };
        var result = converter.ConvertOutputVariable(pythonList, variable);
      
        Assert.IsInstanceOfType(result, typeof(List<object>));
    Assert.AreEqual(3, ((List<object>)result).Count);
    }
    
    // ... more tests ...
}
```

### Integration Tests
```csharp
[TestMethod]
public void CustomStep_WithComplexOutput_PreservesStructure()
{
    // Arrange: Custom step that returns list of dictionaries
    var step = new CustomStep(scriptHost, converter);
    step.Outputs["data"] = new StepVariable
    {
   Type = VariableType.List,
        ItemType = VariableType.Dictionary,
        DictionaryValueType = VariableType.String
    };
    
    // Act: Execute step
    var result = step.ExecuteAsync(context).Result;
    
    // Assert: Output structure preserved
    var output = step.Outputs["data"].Value;
    Assert.IsInstanceOfType(output, typeof(List<object>));
    var firstItem = ((List<object>)output)[0];
    Assert.IsInstanceOfType(firstItem, typeof(Dictionary<string, object>));
}
```

---

## 9. Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|-----------|
| Breaking changes | Low | High | Backward compatible design |
| Performance impact | Low | Medium | Conversion is fast; tested |
| Complex type handling | Medium | Medium | Comprehensive test suite |
| User education | High | Medium | Documentation & examples |

---

## 10. Conclusion

The current implementation treats type information as optional metadata. This document proposes making **types first-class citizens** in the step execution pipeline through:

1. **Type validation on inputs** - Fail fast if wrong type provided
2. **Structured output handling** - Preserve complex types instead of stringifying
3. **Clear error messages** - Help developers understand type mismatches
4. **Extensible framework** - Easy to add new types

This refactoring will eliminate the mysterious type mismatches developers experience and make the system more robust for complex data transformations like the NYTimes headlines example.

---

## Appendix A: Future Enhancements

1. **Type Coercion Strategies**
   - Strict: Fail if type doesn't match exactly
   - Lenient: Attempt automatic conversion
   - Smart: Use heuristics to determine best conversion

2. **Custom Type Definitions**
   - Allow steps to define custom types for outputs
   - JSON Schema support for complex objects

3. **Type Inference**
 - Auto-detect output types from first execution
   - Suggest types to developer

4. **Async Variable Conversion**
   - Support async conversions (file reading, API calls)

5. **Stream/File Handling**
   - Large file transfer between steps
   - Binary data support
