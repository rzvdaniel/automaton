# UI Variable Handling Analysis: Current State & Integration with Proposed Solution

## Overview

This document analyzes how the Blazor UI (`StepPage.razor`) currently handles step variables and shows how the proposed `IScriptVariableConverter` solution integrates with and improves the existing UI.

---

## Current UI Variable Handling

### 1. Variable Definition (Input & Output Tabs)

**Location:** `StepPage.razor` - "Input Variables" & "Output Variables" tabs

```razor
<!-- INPUT VARIABLES TAB -->
<TabPane Tab="Input Variables" Key="2">
    <Table DataSource="@context.Definition.CodeInputVariables">
        <Column Title="Name" TData="string">
       <Input Type="InputType.Text" @bind-Value="inputVariable.Name" AutoFocus />
        </Column>
        <Column Title="Type" TData="string">
            <Select @bind-Value="inputVariable.Type"
           Placeholder="Select type..."
                  TItemValue="VariableType"
    TItem="VariableType">
  <SelectOptions>
   @foreach (var variableType in VariableTypes)
    {
                <SelectOption Value=@variableType Label=@variableType.ToString() />
      }
                </SelectOptions>
     </Select>
        </Column>
        <Column Title="Description">
         <Input Type="InputType.Text" @bind-Value="inputVariable.Data.Description" />
        </Column>
        <Column Title="Value">
        <Input Type="InputType.Text" 
   Value="inputVariable.Data.Value?.ToString()" 
    OnChange="(x) => { inputVariable.Data.Value = x; }" />
        </Column>
 </Table>
</TabPane>
```

**Current Flow:**
1. User defines input variables with Name, Type, Description
2. User enters test values in the "Value" column
3. Type is selected from current `VariableType` enum (String, Text, Boolean, Number, Date)
4. Values are stored as `object` with `.ToString()` conversion

**Issues:**
- ? Only 5 types available (no List, Dict, etc.)
- ? All values converted to/displayed as strings
- ? No validation of type constraints
- ? No support for complex types (nested dicts, lists)

### 2. Variable Testing Panel (Right Side Tab)

**Location:** `StepPage.razor` - "Test Variables" tab

```razor
<TabPane Tab="Test Variables" Key="variables">
    <div class="tab-content-scrollable">
    <div class="variables-panel">
    <Collapse>
                <Panel Key="input">
          <HeaderTemplate><b>Input Variables</b></HeaderTemplate>
         @foreach (var inputVariable in StepDesignerViewModel.CustomStep.Definition.CodeInputVariables)
         {
         <!-- DYNAMICALLY RENDER APPROPRIATE COMPONENT -->
               <DynamicComponent Type="GetVariableComponent(inputVariable.Type)" 
 Parameters="GetVariableComponentParameters(inputVariable)" />
          }
    </Panel>
  <Panel Key="output">
       <HeaderTemplate><b>Output Variables</b></HeaderTemplate>
      @foreach (var outputVariable in StepDesignerViewModel.CustomStep.Definition.CodeOutputVariables)
     {
 <DynamicComponent Type="GetVariableComponent(outputVariable.Type)" 
 Parameters="GetVariableComponentParameters(outputVariable)" />
            }
                </Panel>
     </Collapse>
        </div>
    </div>
</TabPane>
```

**Code-Behind Logic:**
```csharp
private Type GetVariableComponent(VariableType type)
{
    return type switch
    {
        VariableType.String => typeof(StringVariable),
        VariableType.Text => typeof(TextVariable),
     VariableType.Boolean => typeof(BooleanVariable),
        VariableType.Number => typeof(NumericVariable),
     VariableType.Date => typeof(DateVariable),
        _ => throw new NotImplementedException()
    };
}
```

**Current Flow:**
1. For each variable type, a specialized component is rendered
2. Components handle input for their specific type
3. Each component has UI-specific logic for that type
4. Output variables display results after execution

**Issues:**
- ? Only 5 supported types (matches VariableType enum)
- ? No complex type components (List, Dict)
- ? Result values displayed as-is after execution
- ? Output type mismatch if Python returns different type

### 3. Script Execution Results

**Location:** `StepPage.razor` - Execution results

```csharp
// In StepPage.razor.cs
private async Task ExecuteLocally()
{
    StepDesignerViewModel.Execute();
    await Task.CompletedTask;
}

private async Task ExecuteOnRunners()
{
    var results = await StepDesignerViewModel.ExecuteOnRunners(selectedRunners);
    // Results are displayed in Test Variables panel
}
```

**Current Flow:**
1. Code is executed via `StepViewModel.Execute()`
2. Python results are returned as dynamic objects
3. Results are placed into `StepVariable.Value` properties
4. UI components display whatever is in `Value`

**Issues:**
- ? No type conversion of Python results
- ? Complex types (List, Dict) shown as stringified
- ? Output type often doesn't match declared type
- ? No validation that output matches expected type

---

## How the Proposed Solution Improves UI Variable Handling

### Enhancement 1: Extended Type System

**Current:** 5 types  
```csharp
String, Text, Boolean, Number, Date
```

**Proposed:** 20+ types
```csharp
String, Text, Boolean, Integer, Decimal, Number,
Date, DateTime, TimeSpan, Guid,
List, Dictionary, Array, Set,
Object, Json, File, Stream
```

**UI Impact:**
```razor
<!-- BEFORE: Limited type options -->
<Select @bind-Value="inputVariable.Type">
    @foreach (var variableType in new[] { 
        VariableType.String, 
 VariableType.Boolean, 
        VariableType.Number 
    })

<!-- AFTER: Rich type options -->
<Select @bind-Value="inputVariable.Type">
  @foreach (var variableType in VariableTypes)  <!-- 20+ types -->
```

**UI Components Needed:**
```csharp
private Type GetVariableComponent(VariableType type)
{
    return type switch
  {
        // CURRENT (5 types)
        VariableType.String => typeof(StringVariable),
   VariableType.Text => typeof(TextVariable),
        VariableType.Boolean => typeof(BooleanVariable),
     VariableType.Number => typeof(NumericVariable),
        VariableType.Date => typeof(DateVariable),
        
        // NEW (15+ types)
      VariableType.Integer => typeof(IntegerVariable),
VariableType.Decimal => typeof(DecimalVariable),
 VariableType.DateTime => typeof(DateTimeVariable),
        VariableType.TimeSpan => typeof(TimeSpanVariable),
        VariableType.Guid => typeof(GuidVariable),
        
        VariableType.List => typeof(ListVariable),    // NEW!
        VariableType.Dictionary => typeof(DictionaryVariable), // NEW!
        VariableType.Array => typeof(ArrayVariable),  // NEW!
        VariableType.Set => typeof(SetVariable),  // NEW!
        
      VariableType.Object => typeof(ObjectVariable),
     VariableType.Json => typeof(JsonVariable),
     
        _ => throw new NotImplementedException()
 };
}
```

### Enhancement 2: Enhanced StepVariable in UI

**Current Model:**
```csharp
public class StepVariable
{
    public string Id { get; set; }
    public string Name { get; set; }
    public VariableType Type { get; set; }  // Only primary type
    public object? Value { get; set; }
}
```

**Enhanced Model:**
```csharp
public class StepVariable
{
    public string Id { get; set; }
    public string Name { get; set; }
    public VariableType Type { get; set; }              // Primary type
    
    // NEW: Type metadata for collections
    public VariableType? ItemType { get; set; }        // For List<T>
    public VariableType? DictionaryKeyType { get; set; }   // For Dict<K,V>
    public VariableType? DictionaryValueType { get; set; }
    
    // NEW: Validation constraints (shown in UI)
    public bool IsRequired { get; set; }
    public string? ValidationRegex { get; set; }
    public List<string>? AllowedValues { get; set; }
    public decimal? MinValue { get; set; }
  public decimal? MaxValue { get; set; }
    public int? MinLength { get; set; }
    public int? MaxLength { get; set; }
    
    public object? Value { get; set; }
}
```

**UI Integration:**
```razor
<!-- BEFORE: Simple type selection -->
<Column Title="Type" TData="string">
    <Select @bind-Value="inputVariable.Type" />
</Column>

<!-- AFTER: Type selection with validation UI -->
<Column Title="Type" TData="string">
    <Select @bind-Value="inputVariable.Type" OnChange="OnTypeChanged" />
</Column>

<!-- NEW: Show validation constraints -->
@if (inputVariable.Type == VariableType.String)
{
    <FormItem Label="Max Length">
        <InputNumber @bind-Value="inputVariable.MaxLength" />
    </FormItem>
    <FormItem Label="Regex Pattern">
        <Input @bind-Value="inputVariable.ValidationRegex" />
    </FormItem>
}

@if (inputVariable.Type == VariableType.Number)
{
    <FormItem Label="Min Value">
        <InputNumber @bind-Value="inputVariable.MinValue" />
    </FormItem>
    <FormItem Label="Max Value">
        <InputNumber @bind-Value="inputVariable.MaxValue" />
</FormItem>
}

<!-- NEW: Collection type configuration -->
@if (inputVariable.Type == VariableType.List)
{
    <FormItem Label="Item Type">
        <Select @bind-Value="inputVariable.ItemType" />
    </FormItem>
}

@if (inputVariable.Type == VariableType.Dictionary)
{
    <FormItem Label="Key Type">
        <Select @bind-Value="inputVariable.DictionaryKeyType" />
    </FormItem>
    <FormItem Label="Value Type">
        <Select @bind-Value="inputVariable.DictionaryValueType" />
    </FormItem>
}
```

### Enhancement 3: Type-Aware Variable Input Components

**Current Problem:**
```csharp
// All values converted to string
Value="inputVariable.Data.Value?.ToString()" 
OnChange="(x) => { inputVariable.Data.Value = x; }"
```

**Result in Python:**
```python
# Python receives strings for everything, even if declared as Number
api_key = "secret"   # Correct - String
count = "42"           # WRONG - should be Number  
is_active = "True"          # WRONG - should be Boolean
items = "[1,2,3]"           # WRONG - should be List
```

**Proposed Solution:**
```csharp
// Type-specific input components with proper serialization
public class ListVariable : ComponentBase
{
    [Parameter] public StepVariable Variable { get; set; }
    
    private List<object> items;
    
    protected override void OnInitialized()
  {
    // Deserialize from Value using converter
      items = (List<object>)converter.ConvertInputVariable(
    Variable.Value, 
            Variable);
    }
 
    private void OnItemAdded(object item)
    {
     items.Add(item);
        // Serialize back to Value
    Variable.Value = items;
    }
}

public class DictionaryVariable : ComponentBase
{
 [Parameter] public StepVariable Variable { get; set; }
    
    private Dictionary<string, object> dict;
    
    protected override void OnInitialized()
    {
        // Deserialize from Value using converter
        dict = (Dictionary<string, object>)converter.ConvertInputVariable(
          Variable.Value,
  Variable);
    }
    
    private void OnKeyValueAdded(string key, object value)
    {
        dict[key] = value;
        // Serialize back to Value
   Variable.Value = dict;
    }
}
```

**UI Rendering:**
```razor
<!-- Input Variables Tab - Now properly typed -->
@foreach (var inputVariable in StepDesignerViewModel.CustomStep.Definition.CodeInputVariables)
{
    @if (inputVariable.Type == VariableType.List)
    {
        <div class="variable-input">
            <label>@inputVariable.Name</label>
            <button @onclick="() => AddListItem(inputVariable)">Add Item</button>
            <ul>
    @foreach (var item in inputVariable.Value as List<object> ?? new())
         {
<li>@item</li>
                }
            </ul>
 </div>
    }
    else if (inputVariable.Type == VariableType.Dictionary)
    {
   <div class="variable-input">
            <label>@inputVariable.Name</label>
         <button @onclick="() => AddDictItem(inputVariable)">Add Key/Value</button>
        <table>
           @foreach (var kvp in inputVariable.Value as Dictionary<string, object> ?? new())
        {
       <tr><td>@kvp.Key</td><td>@kvp.Value</td></tr>
          }
            </table>
        </div>
    }
else
    {
        <!-- Standard input for scalar types -->
 <DynamicComponent Type="GetVariableComponent(inputVariable.Type)" 
         Parameters="GetVariableComponentParameters(inputVariable)" />
    }
}
```

### Enhancement 4: Type-Safe Input Validation (Before Execution)

**Current Problem:**
```csharp
// No validation before execution
private async Task ExecuteLocally()
{
    StepDesignerViewModel.Execute();  // Just run it, hope types are right
 await Task.CompletedTask;
}
```

**Python Error (later):**
```
TypeError: expected str, got int
Line 42 in script
? Unclear which variable caused the error
```

**Proposed Solution:**
```csharp
// UI validates inputs BEFORE sending to Python
private async Task ExecuteLocally()
{
  try
    {
    // VALIDATE all input variables against their types
      foreach (var inputVar in StepDesignerViewModel.CustomStep.Definition.CodeInputVariables)
        {
            converter.ValidateVariable(inputVar.Value, inputVar);
 // Throws VariableTypeException with clear message if invalid
        }
        
        // CONVERT inputs to Python-compatible types
    var pythonInputs = new Dictionary<string, object>();
        foreach (var inputVar in StepDesignerViewModel.CustomStep.Definition.CodeInputVariables)
      {
   pythonInputs[inputVar.Name] = converter.ConvertInputVariable(inputVar.Value, inputVar);
        }
        
// EXECUTE with validated, converted inputs
        StepDesignerViewModel.Execute(pythonInputs);
        
        await MessageService.SuccessAsync("Execution successful!");
    }
    catch (VariableTypeException ex)
  {
     await MessageService.ErrorAsync(
$"Input validation failed: {ex.Message}\n" +
            $"Variable: {ex.VariableName}\n" +
            $"Expected type: {ex.DeclaredType}\n" +
   $"Received: {ex.ReceivedType?.Name}");
    }
    catch (Exception ex)
    {
        await MessageService.ErrorAsync($"Execution failed: {ex.Message}");
    }
}
```

**UI Feedback:**
```
Before: Python error after execution (unclear)
After: UI error BEFORE execution (crystal clear)

Example:
? "TypeError: expected str, got int" (Line 42, which variable?)
? "Input validation failed: Variable 'api_key' expected String but received Integer"
```

### Enhancement 5: Type-Safe Output Display (After Execution)

**Current Problem:**
```csharp
// Output just displayed as-is
// If Python returned List, it's shown as string

// Python code:
headlines = [
    {"title": "Breaking News", "url": "https://..."},
    {"title": "Another Story", "url": "https://..."}
]

// UI displayed as:
"[{'title': '...', 'url': '...'}]"  ? String representation
```

**Proposed Solution:**
```csharp
// After execution, output is type-converted and validated
private async Task ExecuteOnRunners()
{
    try
    {
   var results = await StepDesignerViewModel.ExecuteOnRunners(selectedRunners);
     
      // CONVERT Python output to declared types
        foreach (var outputVar in StepDesignerViewModel.CustomStep.Definition.CodeOutputVariables)
        {
            if (results.ContainsKey(outputVar.Name))
        {
         var pythonResult = results[outputVar.Name];
       
     // Convert Python result to declared type (preserves structure!)
        outputVar.Value = converter.ConvertOutputVariable(pythonResult, outputVar);
       
             // Validation happens automatically during conversion
  }
        }
        
        // UI now displays with full type information
    }
    catch (VariableTypeException ex)
    {
     await MessageService.ErrorAsync(
$"Output conversion failed: {ex.Message}\n" +
    $"Variable: {ex.VariableName}\n" +
       $"Expected type: {ex.DeclaredType}");
    }
}
```

**UI Display:**
```
Before: String representation
"[{'title': '...', 'url': '...'}]"

After: Structured display using proper component
??????????????????????????????????????????????
? Title       ? Url        ?
??????????????????????????????????????????????
? Breaking News     ? https://...          ?
??????????????????????????????????????????????
? Another Story   ? https://...     ?
??????????????????????????????????????????????
```

---

## Integration Timeline for UI Components

### Phase 1: Foundation (Weeks 1-2)

**What's Needed:**
- ? Extended VariableType enum
- ? Enhanced StepVariable class
- ? IScriptVariableConverter service

**UI Impact:**
- Still uses same 5 types and 5 components
- No visible changes to StepPage.razor yet
- Converter available for backend integration

### Phase 2: UI Enhancements (Weeks 3-4)

**New UI Components Needed:**
```csharp
// New variable components for extended types
StringVariable     // Updated
TextVariable          // Updated
BooleanVariable         // Updated
IntegerVariable         // NEW
DecimalVariable         // NEW
DateTimeVariable        // NEW
TimeSpanVariable        // NEW
GuidVariable            // NEW
ListVariable // NEW - with add/remove items
DictionaryVariable      // NEW - with add/remove key/values
ArrayVariable           // NEW
SetVariable         // NEW
ObjectVariable      // NEW
JsonVariable   // NEW
```

**StepPage.razor Updates:**
```razor
<!-- 1. Update Type Selection to show 20+ types -->
<Select @bind-Value="inputVariable.Type">
    @foreach (var variableType in VariableTypes)  <!-- Now 20+ instead of 5 -->

<!-- 2. Add UI for type metadata (for collections) -->
@if (inputVariable.Type == VariableType.List)
{
    <FormItem Label="Item Type">
        <Select @bind-Value="inputVariable.ItemType" />
    </FormItem>
}

<!-- 3. Update GetVariableComponent() to handle all 20+ types -->
private Type GetVariableComponent(VariableType type)
{
    return type switch
    {
     // 5 existing types
        VariableType.String => typeof(StringVariable),
        VariableType.Text => typeof(TextVariable),
        VariableType.Boolean => typeof(BooleanVariable),
        VariableType.Number => typeof(NumericVariable),
      VariableType.Date => typeof(DateVariable),
        
        // 15+ new types
      VariableType.Integer => typeof(IntegerVariable),
     VariableType.List => typeof(ListVariable),
        VariableType.Dictionary => typeof(DictionaryVariable),
        // ... etc
    };
}

<!-- 4. Inject converter in code-behind -->
[Inject] private IScriptVariableConverter VariableConverter { get; set; }

<!-- 5. Use converter in Execute methods -->
private async Task ExecuteLocally()
{
    // Validate inputs with converter
    foreach (var var in StepDesignerViewModel.CustomStep.Definition.CodeInputVariables)
    {
        VariableConverter.ValidateVariable(var.Value, var);
    }
    
    // Execute...
    
    // Convert outputs with converter
    foreach (var var in StepDesignerViewModel.CustomStep.Definition.CodeOutputVariables)
    {
  var.Value = VariableConverter.ConvertOutputVariable(..., var);
    }
}
```

### Phase 3: Full Integration (Week 5)

**Complete Features:**
- All 20+ types in UI
- Type-specific input components
- Input validation before execution
- Output type conversion after execution
- Rich display of complex types

---

## New UI Component Examples

### Example 1: ListVariable Component

```razor
@* src/Automaton.Studio/Pages/StepDesigner/Components/Variables/ListVariable.razor *@

@using Automaton.Core.Models;
@using Automaton.Core.Enums;
@inject MessageService MessageService
@inject IScriptVariableConverter VariableConverter

<div class="list-variable-input">
    <div class="list-variable-header">
      <span>@Variable.Name</span>
        <Button Size="ButtonSize.Small" Type="ButtonType.Primary" Icon="plus" OnClick="AddItem">
   Add Item
        </Button>
    </div>
    
    <div class="list-variable-items">
    @if (items == null || items.Count == 0)
   {
      <Empty Description="No items added" />
        }
        else
  {
            <div class="items-list">
    @foreach (var (item, index) in items.Select((x, i) => (x, i)))
    {
        <div class="list-item" key="@index">
   <span class="item-index">@(index + 1).</span>
           <div class="item-value">
          @if (Variable.ItemType == VariableType.String)
              {
              <Input @bind-Value="@((string)item)" />
 }
     else if (Variable.ItemType == VariableType.Number)
    {
            <InputNumber @bind-Value="@((dynamic)item)" />
                  }
          else if (Variable.ItemType == VariableType.Boolean)
         {
         <Checkbox @bind-Checked="@((bool)item)" />
           }
  </div>
    <Button Size="ButtonSize.Small" Danger Icon="delete" OnClick="@(() => RemoveItem(index))" />
      </div>
}
       </div>
        }
    </div>
</div>

@code {
    [Parameter]
    public StepVariable Variable { get; set; }

    private List<object> items;

    protected override void OnInitialized()
    {
        // Initialize list from Variable.Value
        items = Variable.Value as List<object> ?? new();
    }

    private void AddItem()
    {
      var defaultValue = Variable.ItemType switch
{
      VariableType.String => string.Empty,
      VariableType.Number => 0,
         VariableType.Boolean => false,
            _ => null
 };
        
    items.Add(defaultValue);
        UpdateVariable();
    }

    private void RemoveItem(int index)
  {
  items.RemoveAt(index);
        UpdateVariable();
    }

    private void UpdateVariable()
  {
      Variable.Value = items;
        StateHasChanged();
    }
}

<style>
    .list-variable-input {
        border: 1px solid #d9d9d9;
        border-radius: 4px;
        padding: 12px;
    }

    .list-variable-header {
        display: flex;
        justify-content: space-between;
        align-items: center;
  margin-bottom: 12px;
 padding-bottom: 8px;
        border-bottom: 1px solid #f0f0f0;
    }

    .list-variable-items {
    display: flex;
        flex-direction: column;
        gap: 8px;
    }

    .list-item {
display: flex;
        align-items: center;
        gap: 8px;
        padding: 8px;
        background: #fafafa;
  border-radius: 4px;
    }

    .item-index {
        font-weight: bold;
        color: #666;
 min-width: 20px;
 }

    .item-value {
      flex: 1;
    }
</style>
```

### Example 2: DictionaryVariable Component

```razor
@* src/Automaton.Studio/Pages/StepDesigner/Components/Variables/DictionaryVariable.razor *@

@using Automaton.Core.Models;
@using Automaton.Core.Enums;

<div class="dict-variable-input">
    <div class="dict-variable-header">
        <span>@Variable.Name (Key/Value Pairs)</span>
        <Button Size="ButtonSize.Small" Type="ButtonType.Primary" Icon="plus" OnClick="AddItem">
    Add Pair
        </Button>
    </div>
    
    <table class="dict-table">
        <thead>
    <tr>
          <th>Key</th>
       <th>Value</th>
         <th>Action</th>
       </tr>
        </thead>
        <tbody>
        @if (items == null || items.Count == 0)
     {
       <tr><td colspan="3">No items added</td></tr>
            }
       else
 {
       @foreach (var (kvp, index) in items.Select((x, i) => (x, i)))
     {
              <tr key="@index">
      <td>
       <Input @bind-Value="kvp.Key" />
          </td>
             <td>
       @if (Variable.DictionaryValueType == VariableType.String)
       {
          <Input @bind-Value="@((string)kvp.Value)" />
           }
        else if (Variable.DictionaryValueType == VariableType.Number)
   {
  <InputNumber @bind-Value="@((dynamic)kvp.Value)" />
     }
         </td>
                  <td>
          <Button Size="ButtonSize.Small" Danger Icon="delete" OnClick="@(() => RemoveItem(index))" />
       </td>
      </tr>
     }
    }
      </tbody>
  </table>
</div>

@code {
    [Parameter]
    public StepVariable Variable { get; set; }

    private List<(string Key, object Value)> items;

  protected override void OnInitialized()
    {
        // Convert from Dictionary to List of tuples for display
    if (Variable.Value is Dictionary<string, object> dict)
{
            items = dict.Select(kvp => (kvp.Key, kvp.Value)).ToList();
        }
        else
        {
        items = new();
        }
    }

    private void AddItem()
    {
    items.Add(("", null));
        UpdateVariable();
    }

    private void RemoveItem(int index)
    {
        items.RemoveAt(index);
    UpdateVariable();
    }

    private void UpdateVariable()
    {
     Variable.Value = items.ToDictionary(x => x.Key, x => x.Value);
        StateHasChanged();
    }
}

<style>
    .dict-table {
        width: 100%;
        border-collapse: collapse;
    }

    .dict-table th,
  .dict-table td {
        padding: 8px;
        border: 1px solid #d9d9d9;
    }
</style>
```

---

## Summary: UI Integration with Proposed Solution

### Current State (Today)
- ? 5 types with specialized UI components
- ? No validation of input types
- ? Complex types shown as strings
- ? Errors occur during Python execution (late feedback)

### After Phase 1 (Backend Ready)
- ? Converter service available
- ? 20+ types in enum
- ? Backend can validate and convert
- ? UI still using 5 types
- ? UI doesn't use converter yet

### After Phase 2 (UI Enhanced)
- ? All 20+ types in UI
- ? Input validation before execution (early feedback)
- ? Output conversion after execution
- ? Type-specific input components for complex types
- ? UI knows about type metadata (ItemType, KeyType, etc.)
- ? Rich display of results

### After Phase 3 (Full Production)
- ? Type-safe end-to-end variable handling
- ? Clear error messages in UI
- ? Complex types work correctly
- ? Python receives correct types
- ? Results displayed with proper structure

---

## Recommended UI Implementation Order

1. **Phase 1 (Backend):** Create converter, extend types
2. **Phase 2a (UI - Types):** Add new type components (List, Dict, etc.)
3. **Phase 2b (UI - Validation):** Inject converter, validate before execution
4. **Phase 2c (UI - Conversion):** Convert outputs after execution
5. **Phase 3 (Polish):** Edge cases, performance, documentation

---

## Files to Create/Modify for UI Integration

| File | Action | Purpose |
|------|--------|---------|
| `StepPage.razor` | MODIFY | Update variable type dropdown, add metadata UI |
| `StepPage.razor.cs` | MODIFY | Inject converter, validate/convert in Execute() |
| `IntegerVariable.razor` | CREATE | Component for Integer input |
| `DecimalVariable.razor` | CREATE | Component for Decimal input |
| `DateTimeVariable.razor` | CREATE | Component for DateTime input |
| `ListVariable.razor` | CREATE | Component for List input/display |
| `DictionaryVariable.razor` | CREATE | Component for Dictionary input/display |
| `VariableInputFactory.cs` | CREATE | Factory for creating variable components |
| `variables/*.razor` | MODIFY | Update existing components to use converter |

---

This integration ensures the UI and backend work seamlessly together to provide type-safe variable handling throughout the entire workflow!
