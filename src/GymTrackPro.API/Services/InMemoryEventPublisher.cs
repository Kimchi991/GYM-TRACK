using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using GymTrackPro.Shared.Interfaces;

namespace GymTrackPro.API.Services;

public class InMemoryEventPublisher : IDomainEventPublisher
{
    private readonly IServiceProvider _serviceProvider;

    public InMemoryEventPublisher(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task PublishAsync<TEvent>(TEvent @event) where TEvent : IDomainEvent
    {
        var handlers = _serviceProvider.GetServices<IDomainEventHandler<TEvent>>();
        foreach (var handler in handlers)
        {
            await handler.HandleAsync(@event);
        }
    }
}
