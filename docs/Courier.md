# How to use Courier messaging

## Publish RemoveCustomStepNotification

```
private readonly IMediator mediator;
await mediator.Publish(new RemoveCustomStepNotification(stepToRemove.Id), CancellationToken.None);
```

## Subscribe/Unsubscribe/Listen RemoveCustomStepNotification

```
using MediatR.Courier;

private ICourier courier;

protected override async Task OnInitializedAsync()
{
    courier.Subscribe<RemoveCustomStepNotification>(HandleRemoveCustomStepNotification);
    await base.OnInitializedAsync();
}

public void Dispose()
{
    courier.UnSubscribe<RemoveCustomStepNotification>(HandleRemoveCustomStepNotification);
}

public async Task HandleRemoveCustomStepNotification(RemoveCustomStepNotification notification, CancellationToken cancellationToken)
{
    // Called when await mediator.Publish(new RemoveCustomStepNotification(stepToRemove.Id), CancellationToken.None); is called
}
```

## RemoveCustomStepNotification class

```
public class RemoveCustomStepNotification : INotification
{
    public Guid StepId { get; private set; }

    public RemoveCustomStepNotification(Guid stepId)
    {
        StepId = stepId;
    }
}
```


