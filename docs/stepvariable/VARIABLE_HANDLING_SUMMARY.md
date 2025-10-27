# Step Variable Handling - Executive Summary

## Problem Statement

Your workflow engine has a **critical type safety gap** when handling step input/output variables for scripts (Python, Batch):

### Current Issues

1. **Input Type Validation: MISSING** ?
   - Step declares: `Inputs["key"] = String`
   - But receives: Boolean or Number
   - Python executes with wrong type ? cryptic runtime errors
   - **No validation occurs before execution**

2. **Output Type Preservation: LOST** ?
   - Step declares: `Outputs["data"] = ???` (No type info for complex types)
   - Python returns: `[{"title": "...", "url": "..."}]`
   - Current code does: `value.ToString()` 
   - Result: `"[{'title': '...'}]"` (string, structure lost!)
   - **Data structure information destroyed**

3. **Type Information: UNUSED** ?
   - `StepVariable.Type` enum exists but is never checked
   - No conversion logic based on declared type
   - **Type safety is theoretical, not practiced**

### Specific Example: NYTimes Headlines

```python
# Python returns a list of dictionaries:
headlines = [
    {"title": "Breaking News...", "url": "https://..."},
    {"title": "Another Story...", "url": "https://..."}
]
```

**Current behavior:**
```csharp
// In CustomStep.RunAsync()
variable.Value = scriptVariables["headlines"].ToString();
// Result: "[{'title': '...', 'url': '...'}]"  ? Lost structure!
```

**Needed behavior:**
```csharp
// Should preserve as List<Dictionary<string, string>>
// So UI can display as table with columns and rows
```

---

## Root Causes

1. **One-Way Type Flow**: Type info exists at step definition but disappears during execution
2. **No Conversion Layer**: No service to convert between C#, Python, and step types
3. **Blind ToString()**: All outputs dumped to string, losing structure
4. **Limited Type System**: Only 5 types (String, Text, Boolean, Number, Date) - no List, Dict, etc.

---

## Proposed Solution (3 Components)

### 1. Extend VariableType Enum

**From (5 types):**
```csharp
String, Text, Boolean, Number, Date
```

**To (20+ types):**
```csharp
String, Text, Boolean, Number, Integer, Decimal, Date, DateTime,
TimeSpan, Guid, Null,   // Scalars
List, Dictionary, Array, Set,          // Collections  
Object, Json, File, Stream         // Complex types
```

**Plus metadata for nested types:**
```csharp
public class StepVariable
{
    public VariableType Type { get; set; }     // Primary type
    public VariableType? ItemType { get; set; } // For List<T>
    public VariableType? DictionaryKeyType { get; set; }    // For Dict<K,V>
    public VariableType? DictionaryValueType { get; set; }
}
```

### 2. Create IScriptVariableConverter

**Purpose:** Convert values between C#, Python, and declared types

**Responsibilities:**
- ? Validate input values match declared types (BEFORE execution)
- ? Convert inputs to Python-compatible types
- ? Convert Python output to declared output types
- ? Preserve complex type structure (List, Dict)
- ? Handle type mismatches with clear errors

**Example usage:**
```csharp
// INPUT VALIDATION & CONVERSION
try 
{
    converter.ValidateVariable(apiKey, inputVariable);
    var pythonValue = converter.ConvertInputVariable(apiKey, inputVariable);
    // Throws VariableTypeException if types don't match
    // Returns properly typed Python object
}

// OUTPUT CONVERSION
var pythonResult = [{"title": "...", "url": "..."}];
var csharpList = converter.ConvertOutputVariable(pythonResult, outputVariable);
// Returns: List<Dictionary<string, object>>
// Structure preserved!
```

### 3. Update CustomStep to Use Converter

```csharp
protected override Task<ExecutionResult> RunAsync(StepExecutionContext context)
{
    // BEFORE: No validation
    var inputs = CodeInputVariables.ToDictionary(x => x.Name, x => x.Value);
    
    // AFTER: Full validation and conversion
  var inputs = ConvertAndValidateInputs();  // ? Validates types!
    
    var results = scriptHost.Execute(resource, inputs);
    
    // BEFORE: ToString() loses structure
    foreach (var var in Outputs)
      var.Value = scriptVariables[var.Id].ToString();
  
    // AFTER: Type-aware conversion
  ConvertAndAssignOutputs(results);  // ? Preserves structure!
}
```

---

## Benefits

| Current | Proposed |
|---------|----------|
| ? Input type mismatches cause cryptic Python errors | ? Validation happens before execution with clear errors |
| ? Output structure lost (List ? string) | ? Structure preserved (List ? List) |
| ? UI can't display complex types properly | ? UI knows exact type structure |
| ? Manual type handling per step | ? Centralized, reusable converter |
| ? Type enum exists but unused | ? Types are enforced and validated |
| ? Debugging is difficult | ? Detailed conversion logs & errors |

---

## NYTimes Example (Before & After)

### BEFORE: Type Mismatch

```csharp
// Step Definition
Outputs = new()
{
    ["headlines"] = new StepVariable 
    { 
        Name = "headlines",
 Type = VariableType.String,  // ? WRONG! It's a list
        Value = null 
    }
};

// Execution Result
// Python: [{"title": "...", "url": "..."}]
// Stored as: "[{'title': '...', 'url': '...'}]"  ? String!
// UI displays: "System.Collections.Generic.List`1[System.Object]"  ? Wrong!
```

### AFTER: Type Safe

```csharp
// Step Definition  
Outputs = new()
{
    ["headlines"] = new StepVariable 
    { 
        Name = "headlines",
  Type = VariableType.List,// ? CORRECT
        ItemType = VariableType.Dictionary,
        DictionaryKeyType = VariableType.String,
   DictionaryValueType = VariableType.String
    }
};

// Execution Result
// Python: [{"title": "...", "url": "..."}]
// Converted to: List<Dictionary<string, object>>
// UI knows: "List of dictionaries with columns: title, url"
// UI displays: Table with 2 columns, N rows  ? Perfect!
```

---

## Implementation Road Map

### Phase 1: Foundation (2 weeks)
- [ ] Extend VariableType enum (20+ types)
- [ ] Enhance StepVariable with nested type info
- [ ] Create IScriptVariableConverter interface
- [ ] Implement converter (100+ test cases)

### Phase 2: Integration (1 week)
- [ ] Update CustomStep to use converter
- [ ] Update ScriptEngineHost
- [ ] Integration tests with real Python code

### Phase 3: Validation (1 week)
- [ ] Test with existing custom steps
- [ ] Edge case fixes
- [ ] Documentation & examples

**Total: 4 weeks**

---

## How It Works (Simplified)

```
INPUT PHASE:
???????????????????????????????????????????
? 1. Get input: api_key = "secret123"  ?
?    Declared type: String           ?
? 2. Validate type     ?
?    ? Is it a string? Yes ? Continue    ?
? 3. Convert to Python-compatible type    ?
?    ? Create PyString("secret123")      ?
? 4. Execute Python       ?
?    ? Python receives correct type  ?
???????????????????????????????????????????

OUTPUT PHASE:
???????????????????????????????????????????
? 1. Python returns:    ?
?    [{"title": "...", "url": "..."}]    ?
? 2. Check declared output type:          ?
?    List<Dictionary<String, String>>   ?
? 3. Convert Python ? C#    ?
?    [...]  ? List<Dictionary<...>>       ?
? 4. Store with type information          ?
?    ? UI knows it's a list of dicts     ?
???????????????????????????????????????????
```

---

## Key Files to Create/Modify

```
New:
  ??? src/Automaton.Core.Scripting/Conversion/
  ?   ??? IScriptVariableConverter.cs    (Interface)
  ?   ??? ScriptVariableConverter.cs     (Implementation)
  ?   ??? VariableTypeException.cs    (Custom exception)

Modified:
  ??? src/Automaton.Core/Enums/VariableType.cs  (Extend enum)
  ??? src/Automaton.Core/Models/StepVariable.cs  (Add type metadata)
  ??? src/Automaton.Steps/CustomStep.cs   (Use converter)

Tests:
  ??? src/UnitTests/Automaton.Core.Scripting.Tests/
      ??? ScriptVariableConverterTests.cs
```

---

## Risk Mitigation

| Risk | How to Mitigate |
|------|-----------------|
| Breaking changes | Design backward compatible; old code still works |
| Complexity | Start with CustomStep only; expand later |
| Performance | Conversion is O(n) where n=number of variables (tiny) |
| User adoption | Comprehensive examples & documentation |

---

## Next Steps

1. ? **Review this document** with your team
2. ? **Decide on scope** (CustomStep only? All steps?)
3. ? **Create conversion service** (Phase 1)
4. ? **Write unit tests** before integration
5. ? **Update CustomStep** (Phase 2)
6. ? **Test with real Python code** (Phase 3)

---

## Questions to Answer

1. **Scope**: Start with CustomStep only, or all steps?
2. **JSON Serialization**: Use Newtonsoft.Json or System.Text.Json?
3. **Type Coercion**: Strict (fail on mismatch) or lenient (auto-convert)?
4. **Error Handling**: Throw exception or mark step as failed?
5. **Timeline**: Can start Phase 1 this week?

---

**Document:** `docs/ARCHITECTURE_StepVariableHandling.md`  
**Date:** October 2024  
**Status:** Ready for implementation
