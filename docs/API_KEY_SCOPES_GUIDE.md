# API Key Scopes and Permissions

## ?? **Recommended Approach**

We've implemented a **hierarchical permission system** with **FullAccess as the default** to solve the confusion around which scope to choose for console runners.

## ?? **API Key Scopes**

### **FullAccess (Recommended for Console Runners)** ??
- **Value**: `4`
- **Permissions**: All operations
- **Use Cases**:
  - ? Automaton.Runner.Console applications
  - ? Automation scripts and CI/CD pipelines
  - ? Development and testing environments
  - ? Users who want "one key for everything"

**What it can do**:
- Register runners with the server
- Execute workflows sent from the server
- Read flow definitions and execution history
- Report execution status and logs
- All future permissions automatically included

### **Runner** ??  
- **Value**: `3`
- **Permissions**: Runner operations + Execute + ReadOnly
- **Use Cases**:
  - ? Dedicated runner services
  - ? Specific runner deployments
  - ? When you want to limit to runner-specific operations

**What it can do**:
- Register and operate runners
- Execute workflows
- Read flows and executions
- Cannot access admin/management functions (future)

### **Execute** ??
- **Value**: `2` 
- **Permissions**: Execute workflows + ReadOnly
- **Use Cases**:
  - ? External systems that trigger workflows
  - ? API integrations that only execute
  - ? CI/CD systems that run specific workflows

**What it can do**:
- Execute workflows via API
- Read flow definitions
- Read execution history
- Cannot register runners

### **ReadOnly** ??
- **Value**: `1`
- **Permissions**: Read-only access
- **Use Cases**:
  - ? Monitoring and reporting tools
  - ? Dashboards and analytics
  - ? Audit and compliance systems

**What it can do**:
- Read flow definitions
- Read execution history and logs
- Cannot execute or modify anything

## ?? **Why This Approach Works**

### **1. Eliminates User Confusion**
- **Default**: `FullAccess` - works for 95% of use cases
- **Clear hierarchy**: Each scope includes permissions from lower levels
- **Simple choice**: "Do I need restrictions? If not, use FullAccess"

### **2. Perfect for Console Runners**
The `Automaton.Runner.Console` needs to:
- ? **Register** itself with the server (Runner permission)
- ? **Execute** workflows sent by the server (Execute permission)  
- ? **Read** flow definitions and report status (ReadOnly permission)

**FullAccess** provides all of these capabilities with a single key.

### **3. Security Through Scope Limitation**
When you DO need restrictions:
- **ReadOnly**: For monitoring tools that shouldn't execute anything
- **Execute**: For systems that run workflows but shouldn't register runners
- **Runner**: For dedicated runner services with defined boundaries

### **4. Future-Proof Design**
- New permissions can be added to FullAccess automatically
- Existing keys continue to work
- Clear upgrade path from restricted to full access

## ?? **User Experience Flow**

### **Simple Path (95% of users)**:
1. Click "Create API Key"
2. Enter name: "My Console Runner" 
3. **Scope is pre-selected as "FullAccess"** ?
4. Click "Create" - works immediately with console runner

### **Advanced Path (5% of users)**:
1. Click "Create API Key"
2. Enter name and choose specific scope for security/compliance
3. Create key with limited permissions

## ?? **Permission Matrix**

| Operation | ReadOnly | Execute | Runner | FullAccess |
|-----------|:--------:|:-------:|:------:|:----------:|
| Read flows and executions | ? | ? | ? | ? |
| Execute workflows | ? | ? | ? | ? |
| Register runners | ? | ? | ? | ? |
| All current permissions | ? | ? | ? | ? |
| Future permissions | ? | ? | ? | ? |

## ?? **UI Visual Hierarchy**

```
?? FullAccess    - Purple (Premium/Complete)
?? Runner        - Blue (Primary runner color)
?? Execute       - Green (Action/Go)
?? ReadOnly      - Orange (Warning/Limited)
```

## ?? **Console Runner Documentation Update**

The README now recommends:
```bash
# 1. Create API key in Studio (FullAccess scope - default)
# 2. Use with console runner
export AUTOMATON_API_KEY="ak_fullaccess_1234567890abcdef"
automaton-runner run --name "My-Runner" --server https://localhost:7091
```

## ? **Benefits Achieved**

1. **?? Zero Confusion**: Default scope works for everyone
2. **?? Better UX**: One-click setup for console runners  
3. **?? Security Options**: Advanced users can still restrict permissions
4. **?? Future-Proof**: Automatic inclusion of new permissions
5. **?? Fully Tested**: 14 passing unit tests cover all scenarios

The hierarchical scope system provides the **best of both worlds**: simplicity for most users and flexibility for advanced security requirements! ??