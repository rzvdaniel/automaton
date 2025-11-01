using Automaton.Core.Models;
using Automaton.Studio.Steps.Sequence;
using Newtonsoft.Json;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Automaton.Studio.Domain;

public abstract class StudioStep : INotifyPropertyChanged
{
    private const int DefaultMargin = 10;

    protected virtual string StepClass { get; set; } = "designer-step";
    protected virtual string SelectedStepClass { get; set; } = "designer-step-selected";
    protected virtual string DisabledStepClass { get; set; } = "designer-step-disabled";

    #region Automaton.Core

    public string Id { get; set; }

    public string Name { get; set; }

    public string DisplayName { get; set; }

    public string Description { get; set; }

    public string MoreInfo { get; set; }

    public string Type { get; set; }

    public string Icon { get; set; }

    public string CancelCondition { get; set; }

    public bool HasProperties { get; set; } = true;

    public bool ShowVariables { get; set; } = true;

    public string NextStepId { get; set; }

    public string ParentId { get; set; }

    public bool IsNew { get; set; }

    public bool IsExecuting { get; private set; }

    public string Title => string.IsNullOrEmpty(DisplayName) ? Name : DisplayName;

    public IDictionary<string, StepVariable> Inputs { get; set; } = new Dictionary<string, StepVariable>();

    public IDictionary<string, StepVariable> Outputs { get; set; } = new Dictionary<string, StepVariable>();

    #endregion

    public StudioDefinition Definition { get; set; }

    public StudioFlow Flow => Definition.Flow;

    public string Class { get; set; }

    public bool Hidden { get; set; }

    public SequenceStep Parent
    {
        get
        {
            var parent = Definition.Steps.SingleOrDefault(x => x.Id == ParentId);

            return parent as SequenceStep;
        }
    }

    public StudioStep()
    {
        Class = StepClass;
    }

    public abstract Type GetDesignerComponent();

    public abstract Type GetPropertiesComponent();

    // String accessor
    public string GetInputValue(string name)
    {
        var existingVariable = Inputs.Values.FirstOrDefault(v => v.Name == name);
        return existingVariable?.Value ?? string.Empty;
    }

    // Generic accessor for backward compatibility (converts from stored string)
    public T GetInputValue<T>(string name)
    {
        var s = GetInputValue(name);
        object result = default(T);

        if (typeof(T) == typeof(string))
        {
            result = s ?? string.Empty;
        }
        else if (typeof(T) == typeof(Guid) || typeof(T) == typeof(Guid?))
        {
            if (Guid.TryParse(s, out var g)) result = g;
        }
        else if (typeof(T) == typeof(int) || typeof(T) == typeof(int?))
        {
            if (int.TryParse(s, out var i)) result = i;
        }
        else if (typeof(T) == typeof(double) || typeof(T) == typeof(double?))
        {
            if (double.TryParse(s, out var d)) result = d;
        }
        else if (typeof(T).IsGenericType && typeof(T).GetGenericTypeDefinition() == typeof(IList<>))
        {
            try
            {
                result = JsonConvert.DeserializeObject<T>(string.IsNullOrWhiteSpace(s) ? "[]" : s);
            }
            catch
            {
                result = Activator.CreateInstance<T>();
            }
        }
        else
        {
            try
            {
                result = JsonConvert.DeserializeObject<T>(s);
            }
            catch
            {
                result = default(T);
            }
        }

        return (T)result;
    }

    // String setter
    public void SetInputValue(string name, string value)
    {
        var existingVariable = Inputs.Values.FirstOrDefault(v => v.Name == name);

        if (existingVariable == null)
        {
            var newVariable = new StepVariable { Id = Guid.NewGuid().ToString(), Name = name, Value = value };
            Inputs[newVariable.Id] = newVariable;
        }
        else
        {
            existingVariable.Value = value;
        }
    }

    // Object setter overload: stores JSON for non-strings
    public void SetInputValue(string name, object value)
    {
        if (value is string str)
        {
            SetInputValue(name, str);
            return;
        }
        var json = JsonConvert.SerializeObject(value);
        SetInputValue(name, json);
    }

    public string GetOutputValue(string name)
    {
        var output = Outputs.Values.FirstOrDefault(v => v.Name == name);
        return output?.Value ?? string.Empty;
    }

    /// <summary>
    /// Adds/Updates the output variable in the step, but also does it
    /// in the flow so other steps can access it.
    /// </summary>
    /// <param name="variable"></param>
    public void SetOutputVariable(StepVariable variable)
    {
        if (Outputs.ContainsKey(variable.Id))
        {
            Outputs[variable.Id] = variable;
        }
        else
        {
            Outputs.Add(variable.Id, variable);
        }

        Flow.SetVariable(variable);
    }

    /// <summary>
    /// Deletes the output variable from the step and the flow.
    /// </summary>
    /// <param name="variable"></param>
    public void DeleteOutputVariable(StepVariable variable)
    {
        if (Outputs.ContainsKey(variable.Id))
        {
            Outputs.Remove(variable.Id);
            Flow.DeleteVariable(variable);
        }
    }

    public void UpdateOutputVariable(StepVariable variable)
    {
        if (Outputs.ContainsKey(variable.Id))
            Outputs.Remove(variable.Id);

        if (Outputs.ContainsKey(variable.Id))
        {
            Outputs[variable.Id] = variable;
        }
        else
        {
            Outputs.Add(variable.Id, variable);
        }

        if (Flow.VariableExists(variable))
            Flow.DeleteVariable(variable);

        Flow.SetVariable(variable);
    }

    public IEnumerable<StepVariable> GetOutputVariables()
    {
        return Outputs.Values;
    }

    public virtual void Select()
    {
        Class = SelectedStepClass;
    }

    public virtual void Unselect()
    {
        Class = StepClass;
    }

    public virtual void Complete()
    {
        IsNew = false;
        Definition.CompleteStep(this);
    }

    public virtual void Created()
    {
        IsNew = true;
    }

    public bool IsSelected()
    {
        return Class == SelectedStepClass;
    }

    public bool IsVisible()
    {
        return !Hidden;
    }

    public bool HasParent()
    {
        return !string.IsNullOrEmpty(ParentId);
    }

    public void SetExecuting()
    {
        IsExecuting = true;
    }

    public void UnsetExecuting()
    {
        IsExecuting = false;
    }

    public int GetMargin()
    {
        var level = GetNestedLevel();

        var stepMargin = level * DefaultMargin;

        return stepMargin;
    }

    public virtual void UpdateParent()
    {
        var prevStep = GetPreviousStep();

        if (prevStep != null)
        {
            if (prevStep is SequenceStep)
            {
                ParentId = prevStep.Id;
            }
            else if (prevStep.HasParent())
            {
                ParentId = prevStep.ParentId;
            }
            else
            {
                ParentId = null;
            }
        }
        else
        {
            ParentId = null;
        }
    }

    public void UpdateVisibility()
    {
        if (HasParent())
        {
            Hidden = Parent.Collapsed;
        }
    }

    private int GetNestedLevel(int level = 1)
    {
        if (HasParent())
        {
            return Parent.GetNestedLevel(++level);
        }

        return level;
    }

    protected StudioStep GetPreviousStep()
    {
        var prevStepIndex = Definition.Steps.IndexOf(this);
        var prevStep = prevStepIndex > 0 ? Definition.Steps.ElementAt(prevStepIndex - 1) : null;

        return prevStep;
    }

    #region INotifyPropertyChanged

    public event PropertyChangedEventHandler PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #endregion
}
