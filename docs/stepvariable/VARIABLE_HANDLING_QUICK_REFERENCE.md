# Quick Reference: Variable Type Handling Refactoring

## The Problem in 30 Seconds

```csharp
// ? CURRENT: Type information is lost
public IDictionary<string, object> Execute(string code, IDictionary<string, object> inputs)
{
    // All inputs are object/dynamic
    // No type validation before execution
    // No structure preservation on output
    // Python gets wrong type? Cryptic error!
}

// ? PROPOSED: Types are validated and preserved
public IDictionary<string, object> Execute(string code, 
    IDictionary<string, object> inputs,
    IDictionary<string, StepVariable> inputDefinitions)  // ? Type info!
{
    // 1. Validate inputs match declared types
    // 2. Convert to Python-compatible types
    // 3. Execute with confidence
    // 4. Preserve structure in output
}
```

---

## What Needs to Change

### 1?? Extend VariableType Enum
```csharp
// ADD these types:
List,      // [1, 2, 3]
Dictionary,  // {"key": "value"}
Array,
Set,
Object,
Json,
File,
Stream
// Plus: Integer (separate from Number), Decimal, DateTime, TimeSpan, Guid
```

### 2?? Enhance StepVariable
```csharp
public class StepVariable
{
    public VariableType Type { get; set; }
    // ADD these:
  public VariableType? ItemType { get; set; }  // For List<T>
    public VariableType? DictionaryKeyType { get; set; }  // For Dict<K,V>
    public VariableType? DictionaryValueType { get; set; }
    
    public bool IsRequired { get; set; }
    public object? DefaultValue { get; set; }
    public List<string>? AllowedValues { get; set; }
    public string? ValidationRegex { get; set; }
}
```

### 3?? Create Converter Service
```csharp
public interface IScriptVariableConverter
{
    // VALIDATE before execution
    object? ConvertInputVariable(object? value, StepVariable variable);
    
    // CONVERT Python output to C# types
    object? ConvertOutputVariable(dynamic pythonResult, StepVariable variable);
    
    // VALIDATE constraints
    bool ValidateVariable(object? value, StepVariable variable);
}
```

### 4?? Update CustomStep
```csharp
public class CustomStep : WorkflowStep
{
    private readonly IScriptVariableConverter converter;  // Inject
    
    private IDictionary<string, object> ConvertAndValidateInputs()
    {
        // For each input variable:
        // 1. Check type matches declared type
      // 2. Convert to Python-compatible type
        // 3. Throw clear error if validation fails
      return converted;
    }
    
    private void ConvertAndAssignOutputs(IDictionary<string, dynamic> results)
    {
        // For each output variable:
   // 1. Get Python result
        // 2. Convert to declared type (preserving structure!)
     // 3. Assign to variable
    }
}
```

---

## Three-Step Type Flow

```
STEP 1: DEFINITION (Before Execution)
??????????????????????????????????????????
? Step Developer Defines:     ?
?  Inputs["api_key"] = StepVariable {    ?
?      Type = String,     ?
?      IsRequired = true,                ?
?      ValidationRegex = "^[a-z0-9]{32}" ?
?  }             ?
?  Outputs["headlines"] = StepVariable { ?
?      Type = List,     ?
?      ItemType = Dictionary,         ?
?      DictionaryKeyType = String,       ?
?  DictionaryValueType = String      ?
?  }             ?
??????????????????????????????????????????

STEP 2: VALIDATION (Before Python Execution)
??????????????????????????????????????????
? Converter.ValidateInputs():   ?
?  ? api_key = "abc123..." (String?)     ?
?  ? Matches regex pattern? Yes!   ?
?  ? Is required? Yes, not null!     ?
?  ? PASS: Continue to execution   ?
?         ?
? If validation FAILS:          ?
?  ? THROW: VariableTypeException        ?
?  ? Stop: Don't run Python           ?
??????????????????????????????????????????

STEP 3: CONVERSION (After Python Execution)
??????????????????????????????????????????
? Python Output:        ?
?  headlines = [         ?
?      {"title": "...", "url": "..."},   ?
?      {"title": "...", "url": "..."}    ?
?  ]  ?
?                 ?
? Converter.ConvertOutputVariable():     ?
?  ? Target type = List<Dictionary>      ?
?  ? Item type = Dictionary          ?
?  ? Dict key type = String ?
?  ? Dict value type = String            ?
?  ? RESULT: List<Dictionary<str, obj>>  ?
?  ? PRESERVE: Full structure  ?
??????????????????????????????????????????
```

---

## Implementation Checklist

### Phase 1: Foundation
- [ ] Create new file: `Conversion/IScriptVariableConverter.cs`
- [ ] Create new file: `Conversion/ScriptVariableConverter.cs`
- [ ] Create new file: `Conversion/VariableTypeException.cs`
- [ ] Update `Enums/VariableType.cs` - add 15+ new types
- [ ] Update `Models/StepVariable.cs` - add ItemType, DictionaryKeyType, etc.
- [ ] Write 50+ unit tests for converter
- [ ] **Est. Time: 5 days**

### Phase 2: Integration
- [ ] Update `CustomStep` - add ConvertAndValidateInputs()
- [ ] Update `CustomStep` - add ConvertAndAssignOutputs()
- [ ] Inject IScriptVariableConverter into CustomStep
- [ ] Update CustomStep.RunAsync() to use converter
- [ ] Write 20+ integration tests
- [ ] **Est. Time: 3 days**

### Phase 3: Testing & Documentation
- [ ] Test with existing custom steps
- [ ] Fix edge cases
- [ ] Create examples for UI developers
- [ ] Write documentation
- [ ] **Est. Time: 2 days**

**Total: ~10 days**

---

## Key Benefits

| Problem | Solution |
|---------|----------|
| Input type mismatches cause cryptic Python errors | Validate before execution with clear error messages |
| Output structure lost (List ? string) | Preserve types (List ? List, Dict ? Dict) |
| UI displays "System.Collections.Generic.List`1" | UI knows exact structure (table with N columns) |
| Manual type handling per step | Centralized, reusable converter |
| Type enum unused | Types are validated, enforced, and logged |
| Hard to debug | Detailed conversion logs and validation errors |

---

## Before & After Example

### BEFORE ?
```csharp
// Step definition
Outputs["headlines"] = new StepVariable { Type = VariableType.String };

// Python returns list of dicts
// Current code: 
variable.Value = scriptVariables["headlines"].ToString();
// Result: "[{'title': '...', 'url': '...'}]"  ? Stringified!

// UI sees:
"[{'title': '...', 'url': '...'}]"  ? Can't parse, can't display as table
```

### AFTER ?
```csharp
// Step definition (type-aware)
Outputs["headlines"] = new StepVariable 
{ 
    Type = VariableType.List,
    ItemType = VariableType.Dictionary,
    DictionaryKeyType = VariableType.String,
 DictionaryValueType = VariableType.String
};

// Python returns list of dicts
// New code:
variable.Value = converter.ConvertOutputVariable(pythonList, variable);
// Result: List<Dictionary<string, object>>  ? Structured!

// UI sees:
// { Type: List, ItemType: Dictionary, Keys: ["title", "url"] }
// Displays as: 2-column table with N rows  ? Perfect!
```

---

## Code Structure After Refactoring

```
src/Automaton.Core.Scripting/
??? Conversion/
?   ??? IScriptVariableConverter.cs       ? NEW
?   ??? ScriptVariableConverter.cs        ? NEW (100+ lines)
?   ??? VariableTypeException.cs          ? NEW
??? Engines/
?   ??? Python/
?   ?   ??? PythonNetScriptEngine.cs  (unchanged)
?   ??? ScriptEngineHost.cs (unchanged)

src/Automaton.Core/
??? Enums/
?   ??? VariableType.cs     ? UPDATED (+15 types)
??? Models/
    ??? StepVariable.cs          ? UPDATED (+4 properties)

src/Automaton.Steps/
??? CustomStep.cs    ? UPDATED (use converter)

Tests/
??? Automaton.Core.Scripting.Tests/
    ??? ScriptVariableConverterTests.cs   ? NEW (50+ tests)
```

---

## FAQ

**Q: Will this break existing custom steps?**  
A: No. The changes are backward compatible. Old code continues to work; new converter is used optionally.

**Q: How much performance impact?**  
A: Negligible. Conversion happens once per variable (typically 5-20 variables). No runtime impact.

**Q: What if Python returns unexpected type?**  
A: Converter throws `VariableTypeException` with clear message. Step fails gracefully with explanation.

**Q: Do I need to update all steps immediately?**  
A: No. Start with CustomStep. Other steps can adopt gradually.

**Q: What about backward compatibility with existing outputs?**  
A: If you have existing flows with string-ified outputs, implement a "legacy mode" that auto-detects and handles them.

---

## Getting Started

1. **Read Full Document**: `docs/ARCHITECTURE_StepVariableHandling.md`
2. **Create Converter Interface**: `IScriptVariableConverter.cs`
3. **Implement Converter**: `ScriptVariableConverter.cs`
4. **Write Unit Tests**: Test each conversion scenario
5. **Update CustomStep**: Use converter in RunAsync()
6. **Integration Test**: Run with real Python code

---

## Success Criteria

? All input variables validated before Python execution  
? No more `ToString()` on complex output types  
? Type mismatches produce clear error messages  
? UI receives complete type information for outputs  
? List and Dictionary types work correctly  
? Backward compatible with existing steps  
? 50+ unit tests pass  
? Integration tests pass  

---

**Questions?** Refer to the full architecture document in `docs/ARCHITECTURE_StepVariableHandling.md`
