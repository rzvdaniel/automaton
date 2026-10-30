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
                    Id = command.Id ?? throw new InvalidOperationException("A user ID is required to update the profile."),
                    Name = command.Name,
                    Email = command.Email ?? throw new InvalidOperationException("An email address is required to update the profile.")
                }
            );

            await dataContext.SaveChangesAsync(cancellationToken);
        }
    }
}
