using Automaton.Api.Data;
using Automaton.Api.Services;
using MediatR;

namespace Automaton.Api.Application.Commands.Handlers
{
    public class UpdateUserInfoCommandHandler : IRequestHandler<UpdateUserInfoCommand>
    {
        private readonly ApplicationDbContext dataContext;
        private readonly UsersService userManager;

        public UpdateUserInfoCommandHandler(ApplicationDbContext dataContext, UsersService userManager)
        {
            this.dataContext = dataContext;
            this.userManager = userManager;
        }

        public async Task Handle(UpdateUserInfoCommand command, CancellationToken cancellationToken)
        {
            await userManager.UpdateProfile(
                new Models.UserProfile
                {
                    Id = command.Id.Value,
                    Name = command.Name,
                    Email = command.Email
                }
            );

            await dataContext.SaveChangesAsync(cancellationToken);
        }
    }
}