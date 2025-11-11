using AntDesign;
using AntDesign.TableModels;
using Automaton.Studio.Models;
using Automaton.Studio.Pages.Flows.FlowDesigner;
using Automaton.Studio.Shared.Components;
using Microsoft.AspNetCore.Components;

namespace Automaton.Studio.Pages.Flows.Designer
{
    public partial class FlowActivity : ComponentBase
    {
        public const int PageStart = 1;
        public const int PageSize = 100;

        [Parameter] public string FlowId { get; set; }
        [Parameter] public string FlowName { get; set; }

        public bool loading;

        [Inject] private NavigationManager NavigationManager { get; set; } = default!;
        [Inject] private FlowActivityViewModel FlowActivityViewModel { get; set; } = default!;
        [Inject] private ModalService ModalService { get; set; }
        [Inject] private MessageService MessageService { get; set; }

        protected override async Task OnInitializedAsync()
        {
            await base.OnInitializedAsync();
            await FlowActivityViewModel.GetFlowActivity(FlowId, PageStart, PageSize);

        }

        async Task OnChange(QueryModel queryModel)
        {
            loading = true;

            try
            {
                await FlowActivityViewModel.GetFlowActivity(FlowId, queryModel.StartIndex, queryModel.PageSize);
            }
            catch
            {
                await MessageService.ErrorAsync(Resources.Errors.FlowsActivityNotLoaded);
            }
            finally
            {
                loading = false;
            }
        }

        private async Task OnRowExpand(RowData<FlowExecution> rowData)
        {
            if (rowData.Expanded)
            {
                rowData.Data.LogsText = await FlowActivityViewModel.GetLogsText(FlowId, rowData.Data.Id);
                StateHasChanged();
            }
        }

        private static bool LogIsLoading(string log)
        {
            return string.IsNullOrEmpty(log);
        }

        private List<string> GetActivityActionCardBullets()
        {
            return new List<string>
            {
                "Real-time execution tracking",
                "Detailed logs for each run",
                "Status and performance metrics",
                "Historical execution data"
            };
        }

        private List<EnhancedEmpty.FeatureCardModel> GetActivityFeatureCards()
        {
            return new List<EnhancedEmpty.FeatureCardModel>
            {
                new() { Title = "Live Monitoring", Description = "Track workflow executions in real-time", Icon = "eye", IconColor = "#3b82f6" },
                new() { Title = "Detailed Logs", Description = "Access comprehensive execution logs", Icon = "file-text", IconColor = "#10b981" },
                new() { Title = "Status Tracking", Description = "Monitor success, failures, and progress", Icon = "dashboard", IconColor = "#f59e0b" }
            };
        }

        private List<EnhancedEmpty.HelpLinkModel> GetActivityHelpLinks()
        {
            return new List<EnhancedEmpty.HelpLinkModel>
            {
                new() { Text = "Monitoring Guide", Url = "#", Icon = "book", Description = "Learn about execution monitoring" },
                new() { Text = "Troubleshooting", Url = "#", Icon = "tool", Description = "Debug failed executions" }
            };
        }
    }
}
