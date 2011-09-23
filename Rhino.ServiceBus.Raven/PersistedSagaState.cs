using System;

namespace Rhino.ServiceBus.Raven
{
    public class PersistedSagaState<TState> : IPersistedSagaIdentifier
    {
        public string Id { get; set; }
        public TState State { get; set; }
    }
}