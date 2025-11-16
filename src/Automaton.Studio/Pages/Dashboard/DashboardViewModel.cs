using Automaton.Studio.Services;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Automaton.Studio.Pages.Dashboard
{
    public class DashboardViewModel
    {
        private readonly ResourceAuthorizationService authorizationService;

        /// <summary>
        /// Check if user can manage resources (update/delete) in the current context.
        /// </summary>
        public bool CanCreateResource => authorizationService.CanManageResources();

        public DashboardViewModel
        (
            ResourceAuthorizationService authorizationService
        )
        {
            this.authorizationService = authorizationService;
        }
    }
}
