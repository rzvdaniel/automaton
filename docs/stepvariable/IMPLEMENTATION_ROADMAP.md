# Implementation Roadmap: Step Variable Type Handling

## Phase 1: Foundation - Type Infrastructure

### 1.1 Extend VariableType Enum

**File:** `src/Automaton.Core/Enums/VariableType.cs`

```csharp
namespace Automaton.Core.Enums;

/// <summary>
/// Comprehensive variable type system for workflow steps.
/// Supports scalar types, collections, and complex types.
/// </summary>
public enum VariableType
{
    // ========== SCALAR TYPES ==========
  /// <summary>Single line string, typically < 500 chars</summary>
    String,

    /// <summary>Multi-line text, typically > 500 chars</summary>
    Text,
    
    /// <summary>Boolean true/false value</summary>
    Boolean,
    
    /// <summary>Generic number (float/double precision)</summary>
    Number,
    
    /// <summary>Whole number without decimals</summary>
    Integer,
    
    /// <summary>Decimal number with fixed precision</summary>
    Decimal,
    
    /// <summary>Date only (no time)</summary>
    Date,
    
    /// <summary>Date and time together</summary>
 DateTime,
    
    /// <summary>Time span/duration</summary>
    TimeSpan,
    
    /// <summary>GUID/UUID identifier</summary>
    Guid,
    
    /// <summary>Null/None value</summary>
    Null,
    
    // ========== COLLECTION TYPES ==========
    /// <summary>List/array of items</summary>
    List,
    
    /// <summary>Dictionary/key-value pairs</summary>
    Dictionary,
    
    /// <summary>Fixed-size array</summary>
    Array,
    
    /// <summary>Unordered set of unique items</summary>
    Set,
    
    // ========== COMPLEX TYPES ==========
    /// <summary>Untyped object (JSON, custom Python object)</summary>
    Object,
    
    /// <summary>JSON-serialized object</summary>
    Json,
    
    /// <summary>File (binary or text)</summary>
    File,
    
    /// <summary>Stream of data</summary>
    Stream
}
```

### 1.2 Enhance StepVariable Class

**File:** `src/Automaton.Core/Models/StepVariable.cs`

```csharp
namespace Automaton.Core.Models;

/// <summary>
/// Enhanced step variable descriptor with complete type information.
/// Used for step inputs, outputs, and workflow variables.
/// </summary>
public class StepVariable
{
 public string Id { get; set; } = Guid.NewGuid().ToString();
 
    public string Name { get; set; }
    
    public string Description { get; set; }
    
    // ========== TYPE INFORMATION ==========
    
    /// <summary>Primary variable type</summary>
    public VariableType Type { get; set; } = VariableType.String;
    
    /// <summary>Item type for List, Array, Set (e.g., List of Strings)</summary>
  public VariableType? ItemType { get; set; }
    
    /// <summary>Key type for Dictionary (usually String)</summary>
    public VariableType? DictionaryKeyType { get; set; }
    
    /// <summary>Value type for Dictionary (can be any type)</summary>
    public VariableType? DictionaryValueType { get; set; }
    
    // ========== VALUE & DEFAULTS ==========
    
  /// <summary>Runtime value of the variable</summary>
    public virtual object? Value { get; set; }
    
    /// <summary>Default value if not provided</summary>
    public object? DefaultValue { get; set; }
    
    // ========== VALIDATION CONSTRAINTS ==========
    
    /// <summary>Is this variable required? Throws if null.</summary>
    public bool IsRequired { get; set; } = false;
    
    /// <summary>Regular expression pattern for string validation</summary>
    public string? ValidationRegex { get; set; }
    
    /// <summary>Allowed values (whitelist). If set, value must be in this list.</summary>
    public List<string>? AllowedValues { get; set; }
    
    /// <summary>Minimum value (for numeric types)</summary>
    public decimal? MinValue { get; set; }
    
    /// <summary>Maximum value (for numeric types)</summary>
    public decimal? MaxValue { get; set; }
    
    /// <summary>Minimum length (for string types)</summary>
public int? MinLength { get; set; }
    
    /// <summary>Maximum length (for string types)</summary>
    public int? MaxLength { get; set; }
    
    /// <summary>Maximum items (for collection types)</summary>
    public int? MaxItems { get; set; }
    
    // ========== METADATA ==========
    
    /// <summary>Display order in UI</summary>
    public int DisplayOrder { get; set; } = 0;
    
    /// <summary>Group name for UI organization</summary>
  public string? GroupName { get; set; }
    
    /// <summary>Custom format hint (e.g., "currency", "percentage")</summary>
public string? FormatHint { get; set; }
    
    // ========== HELPER METHODS ==========
    
    /// <summary>Get the expected .NET CLR type for this variable</summary>
    public Type GetExpectedDotNetType()
    {
   return Type switch
        {
       VariableType.String => typeof(string),
            VariableType.Text => typeof(string),
        VariableType.Boolean => typeof(bool),
            VariableType.Integer => typeof(int),
     VariableType.Decimal => typeof(decimal),
            VariableType.Number => typeof(double),
            VariableType.Date => typeof(DateTime),
         VariableType.DateTime => typeof(DateTime),
      VariableType.TimeSpan => typeof(TimeSpan),
            VariableType.Guid => typeof(Guid),
     VariableType.List => GetListType(),
            VariableType.Array => GetArrayType(),
            VariableType.Dictionary => GetDictionaryType(),
        VariableType.Set => GetSetType(),
            VariableType.Json => typeof(string),
            VariableType.Object => typeof(object),
 VariableType.File => typeof(string),  // File path
      VariableType.Stream => typeof(Stream),
            _ => typeof(object)
  };
    }
    
    private Type GetListType()
    {
    var itemType = ItemType.HasValue 
    ? GetDotNetType(ItemType.Value)
 : typeof(object);
        return typeof(List<>).MakeGenericType(itemType);
    }
    
    private Type GetArrayType()
  {
        var itemType = ItemType.HasValue
         ? GetDotNetType(ItemType.Value)
      : typeof(object);
        return itemType.MakeArrayType();
    }
    
  private Type GetSetType()
    {
        var itemType = ItemType.HasValue
            ? GetDotNetType(ItemType.Value)
: typeof(object);
        return typeof(HashSet<>).MakeGenericType(itemType);
    }
    
    private Type GetDictionaryType()
    {
        var keyType = GetDotNetType(DictionaryKeyType ?? VariableType.String);
        var valueType = GetDotNetType(DictionaryValueType ?? VariableType.Object);
        return typeof(Dictionary<,>).MakeGenericType(keyType, valueType);
    }
    
    private static Type GetDotNetType(VariableType varType) => varType switch
    {
   VariableType.String => typeof(string),
        VariableType.Text => typeof(string),
     VariableType.Boolean => typeof(bool),
        VariableType.Integer => typeof(int),
   VariableType.Decimal => typeof(decimal),
        VariableType.Number => typeof(double),
        VariableType.Date => typeof(DateTime),
        VariableType.DateTime => typeof(DateTime),
        VariableType.TimeSpan => typeof(TimeSpan),
        VariableType.Guid => typeof(Guid),
        VariableType.Null => typeof(object),
        VariableType.Object => typeof(object),
  _ => typeof(object)
    };
}
```

### 1.3 Create Converter Exception

**File:** `src/Automaton.Core.Scripting/Conversion/VariableTypeException.cs`

```csharp
namespace Automaton.Core.Scripting.Conversion;

/// <summary>
/// Thrown when variable type conversion or validation fails.
/// </summary>
public class VariableTypeException : Exception
{
    public string VariableName { get; set; }
    public VariableType? DeclaredType { get; set; }
    public Type? ReceivedType { get; set; }
    
    public VariableTypeException(string message) : base(message) { }
    
    public VariableTypeException(
        string message,
        string variableName,
        VariableType? declaredType = null,
        Type? receivedType = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        VariableName = variableName;
        DeclaredType = declaredType;
        ReceivedType = receivedType;
    }
}
```

---

## Phase 2: Converter Implementation

### 2.1 Create Converter Interface

**File:** `src/Automaton.Core.Scripting/Conversion/IScriptVariableConverter.cs`

```csharp
namespace Automaton.Core.Scripting.Conversion;

/// <summary>
/// Service for converting and validating step variables.
/// Handles type conversions between C#, Python, and declared types.
/// </summary>
public interface IScriptVariableConverter
{
    /// <summary>
    /// Validate that a value matches the variable's type requirements.
    /// </summary>
    /// <exception cref="VariableTypeException">If validation fails</exception>
    void ValidateVariable(object? value, StepVariable variable);
  
    /// <summary>
    /// Convert a value to the declared variable type.
    /// Used for input preparation before Python execution.
    /// </summary>
    /// <exception cref="VariableTypeException">If conversion fails</exception>
    object? ConvertInputVariable(object? value, StepVariable variable);
 
    /// <summary>
    /// Convert Python output to the declared variable type.
    /// Used for result handling after Python execution.
    /// </summary>
    /// <exception cref="VariableTypeException">If conversion fails</exception>
    object? ConvertOutputVariable(dynamic pythonResult, StepVariable variable);
}
```

### 2.2 Implement Converter

**File:** `src/Automaton.Core.Scripting/Conversion/ScriptVariableConverter.cs`

```csharp
namespace Automaton.Core.Scripting.Conversion;

public class ScriptVariableConverter : IScriptVariableConverter
{
    private readonly ILogger logger;
    
    public ScriptVariableConverter()
    {
        logger = Log.ForContext<ScriptVariableConverter>();
    }
    
    public void ValidateVariable(object? value, StepVariable variable)
    {
        if (value == null)
        {
        if (variable.IsRequired)
      {
   throw new VariableTypeException(
            $"Required variable '{variable.Name}' cannot be null",
               variable.Name,
          variable.Type);
        }
       return;
        }
      
        // Validate string constraints
        if (variable.Type == VariableType.String || variable.Type == VariableType.Text)
        {
     ValidateStringVariable(value.ToString() ?? string.Empty, variable);
        }
        
     // Validate numeric constraints
  if (variable.Type == VariableType.Integer || 
            variable.Type == VariableType.Decimal ||
            variable.Type == VariableType.Number)
        {
    ValidateNumericVariable(value, variable);
        }
        
   // Validate collection constraints
        if (variable.Type == VariableType.List ||
    variable.Type == VariableType.Array ||
   variable.Type == VariableType.Set)
      {
            ValidateCollectionVariable(value, variable);
        }
 
        logger.Information("Variable '{Name}' passed validation", variable.Name);
    }
    
    public object? ConvertInputVariable(object? value, StepVariable variable)
    {
    if (value == null)
        {
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
       VariableType.Decimal =>
       ConvertToDecimal(value),
                VariableType.Number =>
         ConvertToDouble(value),
                VariableType.Date or VariableType.DateTime =>
  ConvertToDateTime(value),
      VariableType.TimeSpan =>
    ConvertToTimeSpan(value),
    VariableType.Guid =>
      ConvertToGuid(value),
            VariableType.List =>
    ConvertToList(value, variable),
           VariableType.Array =>
      ConvertToArray(value, variable),
    VariableType.Dictionary =>
            ConvertToDictionary(value, variable),
   VariableType.Json =>
JsonConvert.SerializeObject(value),
         _ => value
   };
     }
        catch (VariableTypeException)
        {
            throw;
        }
   catch (Exception ex)
        {
   throw new VariableTypeException(
        $"Failed to convert input variable '{variable.Name}' " +
           $"from {value?.GetType().Name} to {variable.Type}",
           variable.Name,
    variable.Type,
      value?.GetType(),
          ex);
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
            VariableType.Decimal =>
           Convert.ToDecimal(pythonResult),
VariableType.Number =>
       Convert.ToDouble(pythonResult),
        VariableType.Date or VariableType.DateTime =>
          ConvertPythonToDateTime(pythonResult),
   VariableType.TimeSpan =>
          ConvertPythonToTimeSpan(pythonResult),
   VariableType.Guid =>
  Guid.Parse(pythonResult.ToString()),
    VariableType.List =>
            ConvertPythonToList(pythonResult, variable),
VariableType.Array =>
 ConvertPythonToArray(pythonResult, variable),
  VariableType.Dictionary =>
         ConvertPythonToDictionary(pythonResult, variable),
        VariableType.Json =>
       JsonConvert.SerializeObject(pythonResult),
       _ => pythonResult
    };
        }
      catch (VariableTypeException)
        {
            throw;
        }
catch (Exception ex)
      {
 throw new VariableTypeException(
                $"Failed to convert output variable '{variable.Name}' " +
 $"from Python to {variable.Type}",
    variable.Name,
       variable.Type,
   pythonResult?.GetType(),
           ex);
        }
    }
  
    // ========== PRIVATE CONVERSION METHODS ==========
    
    private string ConvertToString(object value)
        => value as string ?? value.ToString() ?? string.Empty;
    
    private bool ConvertToBoolean(object value)
    {
   if (value is bool b) return b;
  if (bool.TryParse(value?.ToString(), out var result)) return result;
        throw new InvalidOperationException($"Cannot convert {value} to boolean");
    }
    
 private int ConvertToInteger(object value)
    {
        if (value is int i) return i;
        if (int.TryParse(value?.ToString(), out var result)) return result;
        throw new InvalidOperationException($"Cannot convert {value} to integer");
}
    
  private decimal ConvertToDecimal(object value)
    {
   if (value is decimal d) return d;
        if (decimal.TryParse(value?.ToString(), out var result)) return result;
        throw new InvalidOperationException($"Cannot convert {value} to decimal");
    }
  
    private double ConvertToDouble(object value)
    {
        if (value is double d) return d;
        if (double.TryParse(value?.ToString(), out var result)) return result;
        throw new InvalidOperationException($"Cannot convert {value} to double");
    }
    
    private DateTime ConvertToDateTime(object value)
    {
    if (value is DateTime dt) return dt;
        if (DateTime.TryParse(value?.ToString(), out var result)) return result;
 throw new InvalidOperationException($"Cannot convert {value} to datetime");
    }
    
 private TimeSpan ConvertToTimeSpan(object value)
    {
 if (value is TimeSpan ts) return ts;
        if (TimeSpan.TryParse(value?.ToString(), out var result)) return result;
        throw new InvalidOperationException($"Cannot convert {value} to timespan");
    }
    
    private Guid ConvertToGuid(object value)
    {
        if (value is Guid g) return g;
    if (Guid.TryParse(value?.ToString(), out var result)) return result;
    throw new InvalidOperationException($"Cannot convert {value} to guid");
    }
    
    private List<object> ConvertToList(object value, StepVariable variable)
    {
        var list = new List<object>();
        
        if (value is IEnumerable enumerable && !(value is string))
        {
       foreach (var item in enumerable)
      {
       list.Add(item);
          }
        }
        else
        {
            list.Add(value);
  }
        
        return list;
    }
    
    private Array ConvertToArray(object value, StepVariable variable)
    {
    var list = ConvertToList(value, variable);
      var itemType = variable.ItemType.HasValue
     ? GetDotNetType(variable.ItemType.Value)
 : typeof(object);
        return list.Cast<dynamic>().ToArray();
    }
    
    private Dictionary<string, object> ConvertToDictionary(object value, StepVariable variable)
    {
     var dict = new Dictionary<string, object>();
        
        if (value is IDictionary idict)
    {
            foreach (DictionaryEntry entry in idict)
   {
           dict[entry.Key.ToString() ?? string.Empty] = entry.Value ?? new object();
            }
        }
        else if (value is string json && json.StartsWith("{"))
        {
            dict = JsonConvert.DeserializeObject<Dictionary<string, object>>(json) ?? new();
  }

        return dict;
    }
    
    private List<object> ConvertPythonToList(dynamic pythonList, StepVariable variable)
    {
      var result = new List<object>();
        
        try
  {
            foreach (var item in pythonList)
    {
   result.Add(item);
  }
        }
        catch
        {
 // Fallback: treat as single item
       result.Add(pythonList);
        }
        
        return result;
    }
    
    private Array ConvertPythonToArray(dynamic pythonArray, StepVariable variable)
    {
        var list = ConvertPythonToList(pythonArray, variable);
  return list.ToArray();
    }
    
  private Dictionary<string, object> ConvertPythonToDictionary(dynamic pythonDict, StepVariable variable)
    {
        var result = new Dictionary<string, object>();
 
        try
        {
            foreach (var kvp in pythonDict)
            {
            result[kvp.Key.ToString()] = kvp.Value;
        }
        }
        catch
      {
          // Treat as JSON string
   if (pythonDict is string json)
  {
           result = JsonConvert.DeserializeObject<Dictionary<string, object>>(json) ?? new();
       }
     }
        
  return result;
    }
    
    // ... more private validation methods ...
}
```

---

## Phase 3: Integration into CustomStep

### 3.1 Update CustomStep

**File:** `src/Automaton.Steps/CustomStep.cs`

```csharp
namespace Automaton.Steps;

public class CustomStep : WorkflowStep
{
    private readonly ScriptEngineHost scriptHost;
    private readonly IScriptVariableConverter converter;

    [IgnorePropertyParsing(true)]
    public string? Code { get; set; }

    public string Language { get; set; }

    private StepVariableParser StepVariableParser { get; } = new StepVariableParser();

    public IList<StepVariable> CodeInputVariables { get; set; }

    public CustomStep(
   ScriptEngineHost scriptHost,
        IScriptVariableConverter converter)
    {
        this.scriptHost = scriptHost;
        this.converter = converter;
   CodeInputVariables = [];
    }

    protected override async Task<ExecutionResult> RunAsync(StepExecutionContext context)
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

        // IMPROVED: Validate and convert inputs
      var inputVariablesDictionary = ConvertAndValidateInputs();

          var scriptVariables = scriptHost.Execute(resource, inputVariablesDictionary);

     // IMPROVED: Convert and assign outputs with type awareness
            ConvertAndAssignOutputs(scriptVariables, context);

       result.InternalLogs = logs;
            return result;
        }
 catch (VariableTypeException ex)
   {
        logger.Error("Variable type error in step {Name}: {Message}", Name, ex.Message);
    result.Success = false;
            result.Error = ex.Message;
   result.InternalLogs = logs;
            return result;
        }
catch (Exception ex)
        {
            logger.Error(ex, "Error executing custom step {Name}", Name);
   result.Success = false;
            result.Error = ex.Message;
            result.InternalLogs = logs;
            return result;
        }
        finally
  {
            scriptHost.ScriptTextWritten -= OnScriptTextWrittenLocal;
        }
    }

    private IDictionary<string, object> ConvertAndValidateInputs()
    {
        var converted = new Dictionary<string, object>();

        foreach (var variable in CodeInputVariables)
        {
          try
    {
      // Validate type matches declared type
                converter.ValidateVariable(variable.Value, variable);

            // Convert to Python-compatible type
           var convertedValue = converter.ConvertInputVariable(
   variable.Value,
         variable);

        converted[variable.Name] = convertedValue ?? new object();

    logger.Information(
       "Input variable '{Name}' validated and converted",
        variable.Name);
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
        var convertedValue = converter.ConvertOutputVariable(
    pythonResult,
        outputVariable);

                outputVariable.Value = convertedValue;

     logger.Information(
     "Output variable '{Name}' converted and assigned",
           outputVariable.Name);

         context.Workflow.SetVariable(outputVariable);
            }
      catch (VariableTypeException ex)
      {
            logger.Error("Failed to convert output variable '{Name}': {Message}",
         outputVariable.Name, ex.Message);
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

### 3.2 Register in Dependency Injection

**File:** `Program.cs` or `ServiceCollectionExtensions.cs`

```csharp
services.AddScoped<IScriptVariableConverter, ScriptVariableConverter>();
```

---

## Phase 4: Unit Tests

### 4.1 Test Converter

**File:** `src/UnitTests/Automaton.Core.Scripting.Tests/ScriptVariableConverterTests.cs`

```csharp
namespace Automaton.Core.Scripting.Tests;

[TestClass]
public class ScriptVariableConverterTests
{
    private IScriptVariableConverter converter;
    
    [TestInitialize]
    public void Setup()
    {
        converter = new ScriptVariableConverter();
    }
    
// ========== VALIDATION TESTS ==========
    
    [TestMethod]
    public void ValidateVariable_RequiredButNull_Throws()
    {
      var variable = new StepVariable
        {
  Name = "test",
    Type = VariableType.String,
      IsRequired = true
        };
        
Assert.ThrowsException<VariableTypeException>(
() => converter.ValidateVariable(null, variable));
    }
    
    [TestMethod]
    public void ValidateVariable_RegexPattern_ValidatesProperly()
    {
        var variable = new StepVariable
        {
        Name = "email",
   Type = VariableType.String,
            ValidationRegex = @"^[^@]+@[^@]+\.[^@]+$"
   };
        
     // Should pass
        converter.ValidateVariable("test@example.com", variable);
   
        // Should fail
Assert.ThrowsException<VariableTypeException>(
     () => converter.ValidateVariable("invalid-email", variable));
    }
    
    // ========== INPUT CONVERSION TESTS ==========
    
    [TestMethod]
    public void ConvertInputVariable_StringToString_Succeeds()
    {
      var variable = new StepVariable
        {
            Name = "text",
    Type = VariableType.String
        };
 
        var result = converter.ConvertInputVariable("hello", variable);
        
        Assert.AreEqual("hello", result);
    }
    
    [TestMethod]
    public void ConvertInputVariable_IntToInteger_Succeeds()
    {
        var variable = new StepVariable
        {
     Name = "count",
  Type = VariableType.Integer
        };
        
        var result = converter.ConvertInputVariable(42, variable);
    
        Assert.AreEqual(42, (int)result);
    }
    
[TestMethod]
  public void ConvertInputVariable_ListToList_Succeeds()
    {
        var variable = new StepVariable
        {
    Name = "items",
            Type = VariableType.List,
            ItemType = VariableType.String
    };
        
        var input = new List<object> { "a", "b", "c" };
        var result = converter.ConvertInputVariable(input, variable);
        
        Assert.IsInstanceOfType(result, typeof(List<object>));
    Assert.AreEqual(3, ((List<object>)result).Count);
    }
    
    [TestMethod]
    public void ConvertInputVariable_DictToDictionary_Succeeds()
    {
        var variable = new StepVariable
        {
         Name = "data",
         Type = VariableType.Dictionary,
          DictionaryKeyType = VariableType.String,
         DictionaryValueType = VariableType.String
        };
        
 var input = new Dictionary<string, object> { { "key1", "value1" } };
    var result = converter.ConvertInputVariable(input, variable);
        
        Assert.IsInstanceOfType(result, typeof(Dictionary<string, object>));
}
    
    // ========== OUTPUT CONVERSION TESTS ==========
    
    [TestMethod]
    public void ConvertOutputVariable_PythonStringToString_Succeeds()
    {
        var variable = new StepVariable
        {
     Name = "result",
            Type = VariableType.String
        };
   
        dynamic pythonString = "hello from python";
      var result = converter.ConvertOutputVariable(pythonString, variable);
        
        Assert.AreEqual("hello from python", result);
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
        
        dynamic pythonList = new List<object> { "item1", "item2" };
        var result = converter.ConvertOutputVariable(pythonList, variable);
     
      Assert.IsInstanceOfType(result, typeof(List<object>));
        Assert.AreEqual(2, ((List<object>)result).Count);
  }
    
    [TestMethod]
    public void ConvertOutputVariable_PythonDictToDict_Succeeds()
    {
 var variable = new StepVariable
    {
            Name = "data",
 Type = VariableType.Dictionary,
 DictionaryKeyType = VariableType.String,
            DictionaryValueType = VariableType.String
   };
        
        dynamic pythonDict = new Dictionary<string, object>
        {
            { "title", "Test" },
          { "url", "https://example.com" }
        };
        var result = converter.ConvertOutputVariable(pythonDict, variable);
        
        Assert.IsInstanceOfType(result, typeof(Dictionary<string, object>));
        var dict = (Dictionary<string, object>)result;
        Assert.AreEqual("Test", dict["title"]);
 }
  
    // ========== EDGE CASE TESTS ==========
    
    [TestMethod]
    public void ConvertOutputVariable_NullResult_ReturnsNull()
    {
    var variable = new StepVariable { Name = "test", Type = VariableType.String };
        
        var result = converter.ConvertOutputVariable(null, variable);
        
        Assert.IsNull(result);
    }
    
    [TestMethod]
    public void ConvertInputVariable_NullWithDefault_ReturnsDefault()
    {
        var variable = new StepVariable
        {
          Name = "test",
            Type = VariableType.String,
            DefaultValue = "default-value"
    };
        
        var result = converter.ConvertInputVariable(null, variable);
     
        Assert.AreEqual("default-value", result);
    }
}
```

---

## Checklist for Implementation

### Week 1: Foundation
- [ ] Create `VariableType.cs` with 20+ types
- [ ] Update `StepVariable.cs` with type metadata properties
- [ ] Create `VariableTypeException.cs`
- [ ] Create `IScriptVariableConverter.cs` interface
- [ ] Implement `ScriptVariableConverter.cs` (100+ conversion methods)
- [ ] Write 50+ unit tests for converter
- [ ] Code review & adjustments

### Week 2: Integration
- [ ] Update `CustomStep.cs` to inject converter
- [ ] Implement `ConvertAndValidateInputs()` method
- [ ] Implement `ConvertAndAssignOutputs()` method
- [ ] Update `RunAsync()` to use converter
- [ ] Register converter in DI
- [ ] Write 20+ integration tests
- [ ] Test with real Python code

### Week 3: Polish
- [ ] End-to-end testing
- [ ] Edge case fixes
- [ ] Performance testing
- [ ] Documentation & examples
- [ ] Create migration guide for developers

---

## Success Criteria

? All input variables validated before Python execution  
? No more `ToString()` on complex types  
? List and Dictionary types work correctly  
? 70+ unit tests pass  
? 20+ integration tests pass  
? VariableTypeException provides clear error messages  
? Backward compatible (existing steps still work)  
? Documentation complete with examples  

---

## Files Summary

| File | Status | Lines | Purpose |
|------|--------|-------|---------|
| `VariableType.cs` | NEW | 30 | Extended enum with 20+ types |
| `StepVariable.cs` | MODIFIED | 200 | Add type metadata properties |
| `VariableTypeException.cs` | NEW | 20 | Custom exception |
| `IScriptVariableConverter.cs` | NEW | 30 | Converter interface |
| `ScriptVariableConverter.cs` | NEW | 500+ | Full conversion implementation |
| `CustomStep.cs` | MODIFIED | 50 | Use converter in RunAsync() |
| `ScriptVariableConverterTests.cs` | NEW | 200+ | Comprehensive test suite |

