using System.Threading.Tasks;

namespace GymTrackPro.Shared.Interfaces;

public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent @event);
}
