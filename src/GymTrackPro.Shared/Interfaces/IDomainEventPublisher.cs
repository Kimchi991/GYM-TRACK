using System.Threading.Tasks;

namespace GymTrackPro.Shared.Interfaces;

public interface IDomainEventPublisher
{
    Task PublishAsync<TEvent>(TEvent @event) where TEvent : IDomainEvent;
}
