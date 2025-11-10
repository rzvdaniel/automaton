using AntDesign;
using AntDesign.TableModels;
using Automaton.Studio.Enums;
using Automaton.Studio.Models;
using Automaton.Studio.Pages.Flows.FlowDesigner;
using Automaton.Studio.Shared.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Automaton.Studio.Pages.Flows.Designer;

public partial class FlowSchedule : ComponentBase
{
    private bool loading;
    private Form<FlowScheduleModel> form;

    [Parameter] public string FlowId { get; set; }
    [Parameter] public string FlowName { get; set; }

    [Inject] private NavigationManager NavigationManager { get; set; } = default!;
    [Inject] private FlowScheduleViewModel FlowScheduleViewModel { get; set; } = default!;
    [Inject] private ModalService ModalService { get; set; }
    [Inject] private MessageService MessageService { get; set; }

    protected override async Task OnInitializedAsync()
    {
        FlowScheduleViewModel.FlowId = Guid.Parse(FlowId);

        await FlowScheduleViewModel.GetRunners();

        await base.OnInitializedAsync();
    }

    private async Task OnChange(QueryModel queryModel)
    {
        try
        {
            loading = true;

            await FlowScheduleViewModel.GetFlowSchedules(queryModel.StartIndex, queryModel.PageSize);
        }
        catch
        {
            await MessageService.ErrorAsync("Schedules not loaded");
        }
        finally
        {
            loading = false;
        }
    }

    private async Task Save(EditContext editContext)
    {
        var schedule = editContext.Model as FlowScheduleModel;

        try
        {
            schedule.Loading = true;

            if (schedule.IsNew)
            {
                await FlowScheduleViewModel.AddSchedule(schedule);
                await MessageService.InfoAsync("Schedule created");
            }
            else
            {
                await FlowScheduleViewModel.UpdateSchedule(schedule);
                await MessageService.InfoAsync("Schedule updated");
            }
        }
        catch (Exception ex)
        {
            await MessageService.ErrorAsync("Schedule update failed");
        }
        finally
        {
            schedule.Loading = false;
        }
    }

    private async Task DeleteSchedule(Guid id)
    {
        try
        {
            loading = true;

            await FlowScheduleViewModel.DeleteSchedule(id);

            await MessageService.InfoAsync("Schedule deleted");

            StateHasChanged();
        }
        catch (Exception ex)
        {
            await MessageService.ErrorAsync("Schedule delete failed");
        }
        finally
        {
            loading = false;
        }
    }

    private void SaveFailed(EditContext editContext)
    {
        // Do nothing if form validation fails
    }

    private Dictionary<string, object> OnRow(RowData<FlowScheduleModel> row)
    {
        if (row.RowIndex == 1 && row.Data.IsNew)
        {
            row.Expanded = true;
        }

        return new Dictionary<string, object>();
    }

    private void NewSchedule()
    {
        FlowScheduleViewModel.NewSchedule();
    }

    private void OnCronTypeChange(CronType cronType)
    {
    }

    private bool IsMinuteHidden(CronType cronType)
    {
        return cronType == CronType.Never || 
            cronType == CronType.Minutely;
    }

    private bool IsHourHidden(CronType cronType)
    {
        return cronType == CronType.Never || 
            cronType == CronType.Minutely ||
            cronType == CronType.Hourly;
    }

    private bool IsDayHidden(CronType cronType)
    {
        return cronType == CronType.Never || 
            cronType == CronType.Minutely ||
            cronType == CronType.Hourly ||
            cronType == CronType.Daily ||
            cronType == CronType.Weekly;
    }

    private bool IsDayOfWeekHidden(CronType cronType)
    {
        return cronType == CronType.Never || 
           cronType == CronType.Minutely ||
           cronType == CronType.Hourly ||
           cronType == CronType.Daily ||
           cronType == CronType.Monthly ||
           cronType == CronType.Yearly;
    }

    private bool IsMonthHidden(CronType cronType)
    {
        return cronType == CronType.Never || 
            cronType == CronType.Minutely || 
            cronType == CronType.Hourly || 
            cronType == CronType.Daily || 
            cronType == CronType.Weekly ||
            cronType == CronType.Monthly;
    }

    private List<EnhancedEmpty.FeatureCardModel> GetScheduleFeatureCards()
    {
        return new List<EnhancedEmpty.FeatureCardModel>
        {
            new() 
            { 
                Title = "Easily create and manage schedules", 
                Description = "Set up recurring schedules with simple patterns or advanced cron expressions. Our intuitive interface makes scheduling accessible to everyone.", 
                Icon = "calendar",
                IconColor = "#722ed1"
            },
            new() 
            { 
                Title = "Reliable automated execution", 
                Description = "Your workflows run exactly when scheduled with our robust scheduling engine. Monitor execution history and get notifications for any issues.", 
                Icon = "thunderbolt",
                IconColor = "#fa8c16"
            }
        };
    }

    private List<string> GetScheduleActionCardBullets()
    {
        return new List<string>
        {
            "Cron expressions and simple patterns",
            "Recurring and one-time schedules",
            "Execution history and monitoring",
            "Failure notifications and retries"
        };
    }

    private List<EnhancedEmpty.HelpLinkModel> GetScheduleHelpLinks()
    {
        return new List<EnhancedEmpty.HelpLinkModel>
        {
            new() { Text = "Scheduling Guide", Url = "#", Icon = "clock-circle", Description = "Learn how to create and manage automated schedules." },
            new() { Text = "Cron Expressions", Url = "#", Icon = "code", Description = "Master cron syntax for advanced scheduling patterns." },
            new() { Text = "Best Practices", Url = "#", Icon = "bulb", Description = "Discover scheduling best practices and optimization tips." },
            new() { Text = "Monitoring Schedules", Url = "#", Icon = "bar-chart", Description = "Track execution history and monitor schedule performance." }
        };
    }
}
