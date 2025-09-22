using AntDesign;
using Automaton.Core.Enums;
using Automaton.Studio.Domain;
using Automaton.Studio.Services;
using Automaton.Studio.Services.AI;
using Automaton.Studio.Steps.Custom.Variables;
using BlazorMonaco.Editor;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Automaton.Studio.Pages.CustomStepDesigner;

partial class CustomStepPage : ComponentBase, IDisposable
{
    private bool loading = false;
    private bool runningCode = false;
    private bool generatingWithAi = false;
    private bool checkingAiConfiguration = false;
    private bool aiConfigurationChecked = false;
    private bool isAiConfigured = false;
    private string aiDescription = "";
    private string aiSelectedLanguage = "Python";
    private string aiStepName = "";
    private AiGenerationProgress? currentAiProgress;
    private AiGeneratedStepResult? lastAiResult;
    private CancellationTokenSource? aiCancellationTokenSource;

    private Form<CustomStep> form;
    private Form<CustomStep> codeForm;
    private StandaloneCodeEditor codeEditor;

    public IEnumerable<VariableType> VariableTypes { get; } = Enum.GetValues<VariableType>();

    private static readonly List<string> CustomStepLanguages =
    [
        Automaton.Steps.CustomStepLanguage.Python,
        Automaton.Steps.CustomStepLanguage.Batch
    ];

    [Inject] private CustomStepViewModel StepDesignerViewModel { get; set; }
    [Inject] private MessageService MessageService { get; set; }
    [Inject] private AiStepGenerationService AiService { get; set; }
    [Inject] private IAiConfigurationService AiConfigService { get; set; }
    [Inject] private IJSRuntime JSRuntime { get; set; }

    [Parameter] public string StepId { get; set; }

    protected override async Task OnInitializedAsync()
    {
        StepDesignerViewModel.ScriptTextWritten += OnScriptOutputWritten;

        await StepDesignerViewModel.Load(Guid.Parse(StepId));

        // Initialize AI language with current step language
        aiSelectedLanguage = StepDesignerViewModel.CustomStep.Definition.Language;

        // Check AI configuration status
        await CheckAiConfigurationAsync();

        await base.OnInitializedAsync();
    }

    private void SwitchToAiTab()
    {
        // This will be handled by JavaScript to switch to the AI tab
        // For now, we'll just show a message directing users to the AI tab
        MessageService.Info("Switch to the '🤖 AI Generator' tab to generate code with AI.");
    }

    private void AddInputVariable()
    {
        StepDesignerViewModel.AddInputVariable();
    }

    public void DeleteInputVariable(string name)
    {
        StepDesignerViewModel.DeleteInputVariable(name);
    }

    public void AddOutputVariable()
    {
        StepDesignerViewModel.AddOutputVariable();
    }

    public void DeleteOutputVariable(string name)
    {
        StepDesignerViewModel.DeleteOutputVariable(name);
    }

    private async Task Save()
    {
        try
        {
            loading = true;

            if (form.Validate())
            {
                await RefreshCode();
                await StepDesignerViewModel.Save();
                await MessageService.SuccessAsync("Custom step saved successfully!");
            }
        }
        catch (Exception)
        {
            await MessageService.ErrorAsync(Resources.Errors.CustomStepUpdateFailed);
        }
        finally
        {
            loading = false;
        }
    }

    private async Task Run()
    {
        try
        {
            runningCode = true;

            await Task.Delay(100);
            await RefreshCode();

            StepDesignerViewModel.Execute();

            await MessageService.SuccessAsync(Resources.Information.CustomStepExecutionSuccessful);
        }
        catch (Exception ex)
        {
            await MessageService.ErrorAsync($"{Resources.Errors.CustomStepExecutionFailed}. Error: {ex.Message}");
        }
        finally
        {
            runningCode = false;
        }
    }

    private async Task GenerateWithAi()
    {
        if (string.IsNullOrWhiteSpace(aiDescription))
        {
            await MessageService.WarningAsync("Please provide a description of what the custom step should do.");
            return;
        }

        // Check if any AI providers are available first
        var availableProviders = await AiService.GetAvailableProvidersAsync();
        if (!availableProviders.Any())
        {
            await MessageService.ErrorAsync(
                "No AI providers are available. Please configure at least one AI provider in AI Settings.",
                duration: 5000);
            return;
        }

        try
        {
            generatingWithAi = true;
            currentAiProgress = null;
            lastAiResult = null;
            aiCancellationTokenSource = new CancellationTokenSource();

            var progress = new Progress<AiGenerationProgress>(OnAiProgressUpdate);

            var result = await AiService.GenerateStepDefinitionAsync(
                aiDescription,
                aiSelectedLanguage,
                progressCallback: progress,
                cancellationToken: aiCancellationTokenSource.Token);

            lastAiResult = result;

            if (result.IsSuccess)
            {
                // Don't auto-apply, let user review first
                await MessageService.SuccessAsync("Custom step generated successfully! Review the result and click 'Apply to Step' if you're satisfied.");
            }
            else
            {
                await MessageService.ErrorAsync($"Failed to generate custom step: {result.ErrorMessage}");
            }
        }
        catch (OperationCanceledException)
        {
            await MessageService.InfoAsync("AI generation was cancelled.");
        }
        catch (Exception ex)
        {
            await MessageService.ErrorAsync($"An error occurred while generating the custom step: {ex.Message}");
            lastAiResult = new AiGeneratedStepResult
            {
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }
        finally
        {
            generatingWithAi = false;
            aiCancellationTokenSource?.Dispose();
            aiCancellationTokenSource = null;
            StateHasChanged();
        }
    }

    private void CancelAiGeneration()
    {
        aiCancellationTokenSource?.Cancel();
    }

    private async Task ApplyAiGeneration()
    {
        if (lastAiResult?.IsSuccess == true)
        {
            try
            {
                // Apply the generated result to the current custom step
                AiService.ApplyGeneratedResultToCustomStep(StepDesignerViewModel.CustomStep, lastAiResult);

                // Update step name if provided by AI
                if (!string.IsNullOrEmpty(aiStepName))
                {
                    StepDesignerViewModel.CustomStep.Name = aiStepName;
                }

                // Update the Monaco editor with the new code
                if (codeEditor != null && !string.IsNullOrEmpty(lastAiResult.Code))
                {
                    // Use the service's formatting method for consistency
                    var formattedCode = AiStepGenerationService.FormatCodeFromAi(lastAiResult.Code);
                    await codeEditor.SetValue(formattedCode);
                }

                // Refresh the UI to show updated variables
                StateHasChanged();

                await MessageService.SuccessAsync("AI-generated code and variables have been applied to your custom step! The code has been formatted with proper line breaks. Don't forget to save.");
            }
            catch (Exception ex)
            {
                await MessageService.ErrorAsync($"Failed to apply AI generation: {ex.Message}");
            }
        }
    }

    private void ResetAiGeneration()
    {
        currentAiProgress = null;
        lastAiResult = null;
        generatingWithAi = false;
        aiDescription = "";
        aiStepName = "";
        StateHasChanged();
    }

    private async Task NavigateToAiSettings()
    {
        // Open AI Settings in a new tab using JavaScript
        await JSRuntime.InvokeVoidAsync("open", "/ai-settings", "_blank");
    }

    private void ShowAiGenerationPanel()
    {
        // This method is no longer needed as we use the integrated tab
        SwitchToAiTab();
    }

    private void HideAiGenerationPanel()
    {
        // This method is no longer needed as we use the integrated tab
        ResetAiGeneration();
    }

    private async Task RefreshCode()
    {
        if (codeEditor != null)
        {
            StepDesignerViewModel.CustomStep.Definition.Code = await codeEditor.GetValue();
        }
    }

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

    private Dictionary<string, object> GetVariableComponentParameters(Core.Models.StepVariable customStepVariable)
    {
        return new Dictionary<string, object>() { { "Variable", customStepVariable } };
    }

    private void OnScriptOutputWritten(object sender, EventArgs e)
    {
        InvokeAsync(StateHasChanged);
    }

    private StandaloneEditorConstructionOptions EditorConstructionOptions(StandaloneCodeEditor editor)
    {
        return new StandaloneEditorConstructionOptions
        {
            AutomaticLayout = true,
            Language = StepDesignerViewModel.CustomStep.Definition.Language.ToLower(),
            Value = StepDesignerViewModel.CustomStep.Definition.Code
        };
    }

    private void OnAiProgressUpdate(AiGenerationProgress progress)
    {
        currentAiProgress = progress;
        InvokeAsync(StateHasChanged);
    }

    private string GetActivityTagColor()
    {
        return "#1890ff";
    }

    private async Task CheckAiConfigurationAsync()
    {
        if (aiConfigurationChecked)
            return;

        try
        {
            checkingAiConfiguration = true;
            var config = await AiConfigService.GetConfigurationAsync();
            isAiConfigured = config.IsOpenAiConfigured;
        }
        catch (Exception)
        {
            isAiConfigured = false;
        }
        finally
        {
            checkingAiConfiguration = false;
            aiConfigurationChecked = true;
            StateHasChanged();
        }
    }

    private async Task RefreshAiConfiguration()
    {
        aiConfigurationChecked = false;
        await CheckAiConfigurationAsync();
    }

    public void Dispose()
    {
        StepDesignerViewModel.ScriptTextWritten -= OnScriptOutputWritten;
        aiCancellationTokenSource?.Cancel();
        aiCancellationTokenSource?.Dispose();
    }
}
