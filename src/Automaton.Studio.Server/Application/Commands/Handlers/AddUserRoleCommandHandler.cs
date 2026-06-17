using Automaton.Api.Data;
using Automaton.Api.Services;
using MediatR;

namespace Automaton.Api.Application.Commands.Handlers
{
    public class AddUserRoleCommandHandler : IRequestHandler<AddUserRoleCommand>
    {
        private readonly ApplicationDbContext dataContext;
        private readonly UsersService userManagerService;

        public AddUserRoleCommandHandler(ApplicationDbContext dataContext, UsersService userManagerService)
        {
            this.dataContext = dataContext;
            this.userManagerService = userManagerService;
        }
        
        public async Task Handle(AddUserRoleCommand command, CancellationToken cancellationToken)
        {
            await userManagerService.AddRole(command.UserId, command.RoleName);
            await dataContext.SaveChangesAsync(cancellationToken);
        }
    }
}