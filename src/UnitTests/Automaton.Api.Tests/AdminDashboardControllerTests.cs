using Automaton.Api.Data;
using Automaton.Api.Models;
using Automaton.Core.Enums;
using Automaton.Api.Controllers.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Automaton.Api.Entities;

namespace Automaton.Api.Tests;

[TestFixture]
public class AdminDashboardControllerTests
{
    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }
}
